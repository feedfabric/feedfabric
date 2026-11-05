"""Trains the For You ranker from the feed log and publishes it for the API.

    uv run train_ranker.py                       # local Postgres, writes to ../.blobs (LocalBlobStorage in dev)
    uv run train_ranker.py --s3-bucket my-bucket # or to S3, where the API reads it on a server

Each row of "FeedLog" is a post a viewer saw, with the signals the ranker had when it was served. Rather than one
"engaged or not" model there is one LightGBM model per outcome (a head):

    open        they opened it                   like        they liked it
    comment     they commented on it             long_dwell  it stayed on screen 10 seconds or more
    skip        they flicked past in under a second without engaging, or pressed "Not interested"

The API adds the heads' probabilities up with WEIGHTS into one value per post, and sorts by that: comments and long
reads count for more than likes, skips count against. Every head also sees the position the post was served at; the
API scores with position 0, so a post isn't rewarded for having been near the top before. Posts in exploration slots
(shown at random, not by score) count three times as much: they are the only data not shaped by the ranker itself.

--source says whose behaviour to learn from: "real" members only (the default: what production trains on),
"synthetic" (the simulator's users) or "all" (development). Simulated users never leak into a real model by accident,
and latest.json records which it was.

Everything is checked on the newest 20% of the data (a time split, as it will be used on the future), against the
scoring formula the API uses without a model, then exported to ONNX and written as models/ranker/<version>-<head>.onnx
with models/ranker/latest.json listing the heads and weights. The API checks for a new latest.json once a minute.
"""

import argparse
import io
import warnings
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

import lightgbm as lgb
import numpy as np
import onnxruntime as ort
import pandas as pd
import psycopg
from onnxmltools import convert_lightgbm
from onnxmltools.convert.common.data_types import FloatTensorType
from sklearn.metrics import ndcg_score, roc_auc_score

# Must match RankingFeatures.Names in src/FeedFabric.Application/Services/Ranking/RankingFeatures.cs, in order:
# the API refuses a model trained on a different list.
FEATURES = [
    "age_minutes", "follows_author", "author_affinity", "topic_match", "likes", "comments", "impressions", "opens",
    "ctr", "engagement_rate", "avg_dwell_s", "author_followers_log", "has_media", "content_length", "velocity_30m",
    "viewer_author_ctr", "viewer_author_seen", "session_topic_match", "semantic_match", "position",
]

# What each outcome is worth when adding the heads up. Comments and long reads mean more than a like; skips cost.
WEIGHTS = {"open": 0.5, "like": 1.0, "comment": 3.0, "long_dwell": 2.0, "skip": -1.5}
EXPLORED_WEIGHT = 3.0
LONG_DWELL_MS = 10_000
SKIP_MS = 1_000

REPO = Path(__file__).resolve().parent.parent

# eval_set still works; its replacement (eval_X / eval_y) doesn't take DataFrames yet
warnings.filterwarnings("ignore", message=".*eval_set.*is deprecated.*")


def heuristic(f: pd.DataFrame) -> np.ndarray:
    """HeuristicRanker.Score in Python, to compare the model with what it would replace. Keep the two in step."""
    recency = np.exp(-f["age_minutes"] / 240)
    interest = ((1 + 3 * f["topic_match"]) * (1 + 1.5 * np.maximum(0, f["semantic_match"] - 0.2)) * (1 + 2 * f["session_topic_match"])
                * (1 + 0.6 * np.log1p(np.maximum(0, f["author_affinity"]))) * (1 + 0.4 * f["follows_author"]))
    quality = ((1 + 8 * f["engagement_rate"] + 4 * f["ctr"]) * (1 + 0.15 * np.log1p(f["velocity_30m"]))
               * (1 + 0.05 * np.minimum(f["avg_dwell_s"], 20)))
    return (recency * interest * quality).to_numpy()


SIMULATED = "sim-%"


def load(database_url: str, days: int, source: str) -> pd.DataFrame:
    # Simulated members' external IDs start with "sim-"; everyone else is a real member
    who = {"real": 'AND u."ExternalUserId" NOT LIKE %(sim)s', "synthetic": 'AND u."ExternalUserId" LIKE %(sim)s', "all": ""}[source]
    query = f"""
        SELECT f."Features"::text, f."ViewerId", f."ServedAt", f."Opened", f."Liked", f."Commented", f."DwellMs", f."NotInterested", f."Explored"
        FROM "FeedLog" f JOIN "Users" u ON u."Id" = f."ViewerId"
        WHERE f."Seen" AND f."ServedAt" >= now() - make_interval(days => %(days)s) {who}
        ORDER BY f."ServedAt"
    """
    with psycopg.connect(database_url) as conn, conn.cursor() as cur:
        cur.execute(query, {"days": days, "sim": SIMULATED})
        rows = cur.fetchall()
    if not rows:
        sys.exit(f"No feed history from {source} members in the last {days} days yet: nothing to train on."
                 + (" Real members' behaviour builds up once the feed is live; until then the API ranks with the formula." if source == "real" else ""))
    frame = pd.DataFrame(rows, columns=["features", "viewer", "served_at", "opened", "liked", "commented", "dwell_ms", "not_interested", "explored"])
    signals = pd.DataFrame([json.loads(f) for f in frame["features"]])
    # Rows logged before a signal existed don't have it: leave them out rather than pretend it was 0
    complete = signals.reindex(columns=FEATURES).notna().all(axis=1)
    print(f"{len(frame):,} posts seen; {int(complete.sum()):,} logged with every current signal")
    frame, signals = frame[complete].reset_index(drop=True), signals[complete].reset_index(drop=True)

    engaged = frame["opened"] | frame["liked"] | frame["commented"]
    labels = pd.DataFrame({
        "open": frame["opened"],
        "like": frame["liked"],
        "comment": frame["commented"],
        "long_dwell": frame["dwell_ms"] >= LONG_DWELL_MS,
        "skip": frame["not_interested"] | ((frame["dwell_ms"] > 0) & (frame["dwell_ms"] < SKIP_MS) & ~engaged),
    }).astype(int)
    meta = frame[["viewer", "served_at", "explored"]]
    return pd.concat([meta, labels, signals[FEATURES].astype("float32")], axis=1)


def value(labels: pd.DataFrame) -> np.ndarray:
    """What a served post turned out to be worth, by the same weights: the gain NDCG measures rankings against"""
    return np.maximum(0, sum(w * labels[head] for head, w in WEIGHTS.items())).to_numpy()


def ndcg_at_10(frame: pd.DataFrame, gains: np.ndarray, scores: np.ndarray) -> float:
    """Mean NDCG@10 over feed sessions (a viewer's posts served in the same minute) that were worth something"""
    frame = frame.assign(gain=gains, score=scores, session=frame["viewer"].astype(str) + frame["served_at"].dt.floor("min").astype(str))
    values = [ndcg_score([g["gain"].to_numpy()], [g["score"].to_numpy()], k=10)
              for _, g in frame.groupby("session") if len(g) >= 2 and g["gain"].sum() > 0]
    return float(np.mean(values)) if values else float("nan")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--database-url", default=os.environ.get("DATABASE_URL", "postgresql://feedfabric:feedfabric@localhost:5432/feedfabric"))
    parser.add_argument("--days", type=int, default=7, help="Train on the feed log of the last N days")
    parser.add_argument("--min-rows", type=int, default=2000, help="Refuse to train on fewer seen posts than this")
    parser.add_argument("--source", choices=["real", "synthetic", "all"], default="real",
                        help="Whose behaviour to learn from: real members (default, for production), the simulator's users, or both (development)")
    parser.add_argument("--blob-root", default=str(REPO / ".blobs"), help="LocalBlobStorage's folder (the API's BlobStorage:Local:Root)")
    parser.add_argument("--s3-bucket", help="Write to this S3 bucket instead (BlobStorage:S3:Bucket); credentials from the usual AWS chain")
    parser.add_argument("--s3-endpoint", help="For S3-compatible storage, e.g. http://localhost:4566 for LocalStack")
    args = parser.parse_args()

    data = load(args.database_url, args.days, args.source)
    print(f"Training on {args.source} members' behaviour")
    if len(data) < args.min_rows:
        sys.exit(f"Not enough data yet (need {args.min_rows:,} seen posts with every signal). Let the simulator run for a while.")
    print("Outcome rates:", ", ".join(f"{h} {data[h].mean():.1%}" for h in WEIGHTS), f"| explored {data['explored'].mean():.1%}")

    # Train on the older 80%, test on the newest 20%: the model will be used on posts it hasn't seen yet
    split = int(len(data) * 0.8)
    train, test = data.iloc[:split], data.iloc[split:]
    sample_weight = np.where(train["explored"], EXPLORED_WEIGHT, 1.0)

    version = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    heads, files, metrics, gains = {}, {}, {}, np.zeros(len(FEATURES))
    test_value = np.zeros(len(test))
    for head, weight in WEIGHTS.items():
        if train[head].sum() < 30 or test[head].nunique() < 2:
            print(f"  {head}: too few examples yet, left out")
            continue
        model = lgb.LGBMClassifier(n_estimators=400, learning_rate=0.05, num_leaves=31, min_child_samples=50,
                                   subsample=0.8, subsample_freq=1, colsample_bytree=0.9, verbose=-1)
        model.fit(train[FEATURES], train[head], sample_weight=sample_weight,
                  eval_set=[(test[FEATURES], test[head])], eval_metric="auc",
                  callbacks=[lgb.early_stopping(30, verbose=False)])

        # Scored at position 0, as the API does
        at_top = test[FEATURES].assign(position=0.0)
        probabilities = model.predict_proba(at_top)[:, 1]
        test_value += weight * probabilities
        metrics[f"auc_{head}"] = round(roc_auc_score(test[head], model.predict_proba(test[FEATURES])[:, 1]), 4)
        gain = model.booster_.feature_importance(importance_type="gain")
        gains += abs(weight) * gain / max(gain.sum(), 1e-9)

        onnx_model = convert_lightgbm(model, initial_types=[("features", FloatTensorType([None, len(FEATURES)]))], zipmap=False, target_opset=15)
        onnx_bytes = onnx_model.SerializeToString()
        check = ort.InferenceSession(onnx_bytes, providers=["CPUExecutionProvider"]).run(["probabilities"], {"features": at_top.to_numpy(np.float32)})[0][:, 1]
        # ONNX compares split thresholds in 32-bit floats and LightGBM in 64-bit, so the odd row exactly on a
        # threshold can go the other way. A small average difference is that; a large one is a broken export.
        difference = np.abs(check - probabilities)
        if difference.mean() > 1e-3 or difference.max() > 0.05:
            sys.exit(f"The {head} head's ONNX export scores differently from LightGBM (mean {difference.mean():.5f}, max {difference.max():.4f}); not publishing.")
        key = f"models/ranker/{version}-{head}.onnx"
        heads[head], files[key] = key, onnx_bytes
        print(f"  {head}: AUC {metrics[f'auc_{head}']} (ONNX matches LightGBM to {difference.mean():.6f} on average)")

    if not heads:
        sys.exit("No head had enough examples to train. Let the simulator run for a while.")

    gain_by_session = value(test)
    metrics["value_ndcg_at_10"] = round(ndcg_at_10(test, gain_by_session, test_value), 4)
    metrics["heuristic_value_ndcg_at_10"] = round(ndcg_at_10(test, gain_by_session, heuristic(test)), 4)
    # The single "engaged" view, as before, so versions can be compared
    engaged = (test["open"] | test["like"] | test["comment"]).astype(int)
    metrics["auc"] = round(roc_auc_score(engaged, test_value), 4)
    metrics["heuristic_auc"] = round(roc_auc_score(engaged, heuristic(test)), 4)
    print("Test (newest 20%):", json.dumps(metrics))

    importance = {name: round(float(g / gains.sum()), 4) for name, g in sorted(zip(FEATURES, gains), key=lambda x: -x[1])}
    latest = {
        "version": version,
        "heads": heads,
        "weights": {h: WEIGHTS[h] for h in heads},
        "features": FEATURES,
        "trainedAt": datetime.now(timezone.utc).isoformat(),
        "trainingRows": len(train),
        "positiveRate": round(float((data["open"] | data["like"] | data["comment"]).mean()), 4),
        "exploredShare": round(float(data["explored"].mean()), 4),
        "source": args.source,
        "metrics": metrics,
        "importance": importance,
    }
    files["models/ranker/latest.json"] = json.dumps(latest, indent=2).encode()
    publish(args, files)
    print(f"Published {len(heads)} heads as {version}; the API picks them up within a minute.")
    print("Importance:", ", ".join(f"{k} {v:.0%}" for k, v in list(importance.items())[:6]))


def publish(args: argparse.Namespace, files: dict[str, bytes]) -> None:
    """The models first, latest.json last, so the API never points at a model that isn't there yet"""
    ordered = sorted(files.items(), key=lambda kv: kv[0].endswith("latest.json"))
    for key, data in ordered:
        content_type = "application/json" if key.endswith(".json") else "application/octet-stream"
        if args.s3_bucket:
            import boto3

            boto3.client("s3", endpoint_url=args.s3_endpoint).upload_fileobj(
                io.BytesIO(data), args.s3_bucket, key, ExtraArgs={"ContentType": content_type})
            continue
        path = Path(args.blob_root) / key
        path.parent.mkdir(parents=True, exist_ok=True)
        temp = path.with_suffix(path.suffix + ".tmp")
        temp.write_bytes(data)
        temp.replace(path)
        # LocalBlobStorage keeps each file's content type beside it
        Path(f"{path}.content-type").write_text(content_type)


if __name__ == "__main__":
    main()
