"""Fine-tunes Detoxify's unbiased toxicity model on this app's wording and publishes it for the API.

    uv run --group tune tune_toxicity.py                        # to ../.blobs (LocalBlobStorage in dev)
    uv run --group tune tune_toxicity.py --s3-bucket my-bucket  # or to S3, where a server reads them

Why: unitary/unbiased-toxic-roberta reads sport talk as violence. "Kill it at the race tomorrow everyone!" scores 0.97
for threat, as high as a real death threat, so no threshold separates them. It also scores friendly swearing ("this
hill is a bitch") as an insult, and sexual-health posts as sexually explicit.

How: mostly distillation, so the model keeps what it knows. On Civil Comments (the data Detoxify trained on, CC0) and
the simulator's posts, it learns to give the scores the original model gives. Mixed in are generated sentences that
teach the difference: sport idioms, friendly swearing and health talk as fine; threats, abuse and hate that use the
same words as offensive. Some sentence patterns are kept out of training entirely and used to test it.

It publishes only if the tuned model, as the API runs it (ONNX, 8-bit), still ranks Civil Comments as well as the
original (ROC AUC within MAX_AUC_DROP per label), is no worse than the original on the held-out sentences (no more
fine ones stopped, beyond MAX_FALSE_FLAG_RATE, and no fewer offensive ones caught), and gets every sentence in CHECKS
right. Each run's model is saved under --cache first, so a run that passes can be published later without training
again: uv run --group tune tune_toxicity.py --publish-saved <folder>
"""

import argparse
import hashlib
import io
import json
import random
import re
import tempfile
import time
import urllib.request
from pathlib import Path

import numpy as np
import onnxruntime as ort
import pandas as pd
import torch
from onnxruntime.quantization import QuantType, quantize_dynamic
from sklearn.metrics import roc_auc_score
from transformers import AutoModelForSequenceClassification, AutoTokenizer

REPO = Path(__file__).resolve().parent.parent
PREFIX = "models/safety/toxicity"

BASE_REPO = "unitary/unbiased-toxic-roberta"
BASE_REVISION = "36295dd80b422dc49f40052021430dae76241adc"
DATA_REPO = "google/civil_comments"
DATA_REVISION = "f2970eb3a55777454c94069077cc8d9b5866312d"
POSTS = REPO / "src/FeedFabric.Simulator/Data/posts.tsv"

LABELS = [
    "toxicity", "severe_toxicity", "obscene", "identity_attack", "insult", "threat", "sexual_explicit",
    "male", "female", "homosexual_gay_or_lesbian", "christian", "jewish", "muslim", "black", "white",
    "psychiatric_or_mental_illness",
]
OFFENCES = LABELS[:7]
COL = {label: i for i, label in enumerate(LABELS)}

# Keep in step with ModerationOptions.ToxicityRejectAt, ToxicityHoldAt and ToxicityFlagAt: the scores at which the API
# blocks a post (threat), holds it for review, or flags it (insult). The tests treat them all as acting on it.
LIMITS = {"severe_toxicity": 0.5, "threat": 0.6, "identity_attack": 0.6, "insult": 0.9, "sexual_explicit": 0.9}

MAX_TOKENS = 256
MAX_AUC_DROP = 0.01
MAX_FALSE_FLAG_RATE = 0.02

# ---------------------------------------------------------------------------------------------------------------------
# Generated sentences. Each family is a list of frames; {event}, {who} and {when} are filled in. Families in
# HELD_OUT_FAMILIES, and frames marked with a leading "~", never go into training: they test whether the model learnt
# the idea rather than the sentences.

EVENTS = [
    "race", "10k", "5k", "half marathon", "marathon", "parkrun", "triathlon", "spin class", "swim session",
    "deadlift session", "leg day", "hill reps", "interval session", "sportive", "netball match", "football match",
    "boxing class", "Hyrox", "trail run", "time trial", "climbing comp", "rowing race", "HIIT class", "bootcamp",
]
WHO = ["everyone", "team", "lads", "ladies", "girls", "guys", "mate", "Sarah", "Tom", "Priya", "squad", "coach", "all"]
WHEN = ["tomorrow", "this weekend", "on Sunday", "tonight", "next week", "today"]
GROUPS = ["muslims", "gay people", "black people", "jews", "trans people", "immigrants", "women", "disabled people"]

# Fine: sport idioms that sound violent
IDIOMS = {
    "kill": [
        "Kill it at the {event} {when}, {who}!", "{Who}, go kill it at the {event}!", "You absolutely killed it at the {event}!",
        "Killing it lately {who}, that {event} was something else", "That {event} nearly killed me 😂",
        "The {event} killed my legs but so worth it", "Killer {event} this morning, legs are gone",
        "This {event} is killing me", "Coach is going to kill us at the {event} {when}", "Go kill your {event} {when}!",
        "~You're going to kill it at the {event}, {who}", "~I'd kill for a coffee after that {event}",
        "~Absolute killer of a {event}, well done {who}",
    ],
    "smash": [
        "You smashed it at the {event}!", "Smash it {when}, {who}!", "Smashing your {event} goals {who}",
        "Absolutely smashed that {event}", "~Go smash the {event} {when}!", "~{Who}, you smashed your PB at the {event}",
    ],
    "destroy": [
        "That {event} absolutely murdered me", "The hills destroyed me at the {event}", "The {event} destroyed my quads",
        "Murdered my PB at the {event} {when}", "{Who} destroyed everyone at the {event}, so fast",
        "~The {event} wrecked me, in a good way", "~That {event} annihilated my calves",
    ],
    "fight": [
        "Attack the hills at the {event}, {who}!", "Fight for every second at the {event}",
        "Take no prisoners at the {event} {when}", "Going to war with the treadmill before the {event}",
        "Shoot for a PB at the {event} {when}", "Beast mode at the {event} {when}, {who}",
        "~Go to battle with that last hill at the {event}", "~Hunt down a PB at the {event}, {who}",
    ],
    "dead": [
        "I'm dead after that {event}", "Died on the last hill of the {event}", "Dying after the {event} but proud",
        "My legs are dead after the {event}", "Knock 'em dead at the {event}, {who}!", "Break a leg at the {event} {when}!",
        "~RIP my legs after the {event}", "~Totally dead on my feet after the {event} 😅",
    ],
    "crush": [
        "You crushed that {event}!", "Crush it at the {event} {when}, {who}!", "Crushing it, {who}, see you at the {event}",
        "Crushed my {event} time by two minutes",
    ],
    "slay": ["Slay at the {event} {when}, {who}!", "You slayed that {event}", "Slaying the {event} {when}"],
}

# Fine: swearing about the sport, not at anyone. Obscene may stay high; insult, threat and attack must not.
SWEARING = [
    "This {event} is a bitch but I love it", "Holy shit what a {event}, new PB", "That was a fucking brilliant {event}!",
    "Fuck this weather, still did my {event}", "Bloody knackered after the {event}, damn hills",
    "Absolute shit timing, my watch died at the {event}", "The {event} was hard as hell", "Pissing down at the {event} and I loved it",
    "~Shit, that {event} was tough", "~Fucking freezing at the {event} but we got it done", "~That last hill is a bastard",
]

# Fine: health talk that mentions sex and bodies
HEALTH = [
    "Sex after pregnancy hurt for months, nobody warns you. Talk to your GP.", "Erectile dysfunction after prostate surgery, anyone else dealing with this?",
    "Painful sex after menopause, my GP prescribed a cream that helped", "Low libido on antidepressants, anyone else?",
    "Pelvic floor exercises fixed my bladder leaks after giving birth", "Testicular self-checks take 2 minutes, lads please do them",
    "Breast cancer survivor here, back to lifting after my mastectomy", "Struggling with my sexuality and it's wrecking my sleep",
    "Vaginal dryness during perimenopause is so common and nobody talks about it", "Cycling long distances made my groin numb, saddle fit sorted it",
    "Sex was the last thing on my mind after chemo, it does come back", "My libido dropped when I overtrained for the {event}",
    "Is it normal to get chafing on your nipples on a long {event}? Asking for my husband",
    "Postnatal physio sorted my pelvic pain and I'm back at the {event}", "Got my smear test done, it took five minutes, please book yours",
    "Penis numbness after long rides is a sign your saddle is wrong, get a bike fit", "Testosterone dropped when I was underfuelling for the {event}",
    "Menopause night sweats wrecked my sleep before the {event}", "Bleeding after sex? Please see a doctor, it was cervical ectropion for me",
    "~Sex drive came back once my iron levels were sorted", "~Period pain wiped me out before the {event}, back at it now",
    "~Prostate exam was nothing to worry about, get checked", "~Vulval pain made cycling to the {event} hell until I saw a specialist",
]

# Offensive: threats, using the same words
THREATS = [
    "I know where you live and I'm going to kill you", "I'm going to kill you, {who}",
    "I'll find you after the {event} and hurt you", "I know which {event} you go to. I'll be waiting, watch your back",
    "Next time I see you at the {event} I'll smash your face in", "I'll crush your skull if you post that again",
    "I'm going to break your legs so you never run again", "You're dead when I see you at the {event}",
    "Someone should kill you, {who}", "I'll slit your throat", "I'm going to beat you until you can't walk",
    "I'll burn your house down with you in it", "I will murder your family", "Keep posting and I'll destroy your face",
    # Threats with no violent word in them
    "I'm coming for you, {who}, and you won't see me coming", "Watch yourself at the {event}, I know what you look like",
    "You'll regret posting that when I find you at the {event}", "Sleep with one eye open, {who}",
    "I know your car and where you park for the {event}. See you soon", "Nobody will find you after the {event}",
    "~I'll put you in hospital, {who}",
    "~Say that again and I'll stab you at the {event}", "~I'm coming for you after the {event} and you won't walk away",
    "~I'll be waiting outside the {event} for you, {who}",
]

# Offensive: abuse aimed at a person
INSULTS = [
    "You're a worthless pathetic idiot, {who}", "You run like a fat pig", "Nobody wants you at the {event}, you ugly loser",
    "Shut up you stupid moron", "You're so ugly nobody will ever love you", "Get off the track you useless waste of space",
    "You're a disgusting slob, stop posting", "What a pathetic loser, everyone at the {event} laughs at you",
    "~You're a brainless cow, {who}", "~Fat ugly freak, stay away from the {event}",
]

# Offensive: hate about who someone is
HATE = [
    "All {group} are terrorists and should be deported", "{Group} are disgusting animals",
    "{Group} shouldn't be allowed at the {event}", "{Group} are a disease on this country",
    "Kick all the {group} out of the {event}", "No {group} at our {event}, they don't belong here",
    "{Group} ruin every {event} they turn up to", "Keep {group} away from our club",
    "~Get {group} out of our {event}, they ruin everything", "~{Group} have no place at the {event}, send them back",
]

HELD_OUT_FAMILIES = {"crush", "slay"}

# Every sentence here must come out as expected: True means the API stops it at LIMITS
CHECKS = [
    ("First gentle walk since the injury, 40 minutes, legs like jelly. Proud of myself.", False),
    ("Bloody knackered after that run, damn hills. Worth it though.", False),
    ("Day 21 of no screens after 9 and I sleep so much better", False),
    ("What the hell, my watch died at mile 12. Absolute shit timing 😂", False),
    ("As a gay man, finding a running club that felt safe took me years.", False),
    ("I'm a Muslim woman and I train in a hijab, ask me anything.", False),
    ("Black runners deserve more visibility in this sport.", False),
    ("My therapist says my anxiety improves when I swim. #mentalhealth", False),
    ("Christians, Jews, atheists: everyone welcome at Sunday parkrun.", False),
    ("That was a fucking brilliant run, new PB!", False),
    ("Holy shit what a PB, buzzing", False),
    ("This hill is a bitch but I love it", False),
    ("Fuck this weather, still got my 5k done", False),
    ("Erectile dysfunction after prostate surgery, anyone else dealing with this?", False),
    ("Breast cancer survivor here, back to lifting after my mastectomy", False),
    ("Sex after pregnancy hurt for months, nobody warns you. Talk to your GP.", False),
    ("Pelvic floor exercises fixed my bladder leaks after giving birth", False),
    ("Testicular self-checks take 2 minutes, lads please do them", False),
    ("Struggling with my sexuality and it's wrecking my sleep", False),
    ("You look hot in those running shorts 😉", False),
    ("Kill it at the race tomorrow everyone!", False),
    ("That workout absolutely murdered me", False),
    ("I know where you live and I am going to kill you", True),
    ("you are a worthless pathetic idiot and everyone hates you, just disappear", True),
    ("Shut up you stupid moron", True),
    ("I hope you get hit by a car on your next run", True),
    ("all muslims are terrorists and should be deported", True),
    ("gay people are disgusting and shouldnt be allowed near kids", True),
    ("send me nudes and I'll show you my dick", True),
    ("fuck you, you fucking piece of shit", True),
    ("y0u are a w0rthless 1diot", True),
    ("I want to rip your clothes off and fuck you", True),
    ("You're so ugly nobody will ever love you", True),
]


def fill(frame: str, rng: random.Random) -> str:
    event, who, group = rng.choice(EVENTS), rng.choice(WHO), rng.choice(GROUPS)
    text = frame.lstrip("~").format(event=event, who=who, Who=who.capitalize(), when=rng.choice(WHEN), group=group, Group=group.capitalize())
    return text[0].upper() + text[1:]


def generated(rng: random.Random) -> tuple[list[dict], list[dict]]:
    """Train and held-out sentences, each {text, kind, family}; kind says how its targets are set. Each family gets
    `budget` training sentences however many frames it has, so a frame with no slots is repeated rather than rare."""
    families = [(f"idiom:{name}", "fine", frames, 300) for name, frames in IDIOMS.items()]
    families += [("swearing", "swearing", SWEARING, 600), ("health", "health", HEALTH, 500),
                 ("threat", "threat", THREATS, 900), ("insult", "insult", INSULTS, 600), ("hate", "hate", HATE, 300)]
    checks = {normalise(text) for text, _ in CHECKS}
    train, held_out = [], []
    for family, kind, frames, budget in families:
        held = family.removeprefix("idiom:") in HELD_OUT_FAMILIES
        # A CHECKS sentence never goes into training, so CHECKS stays a fair test
        training = [f for f in frames if not held and not f.startswith("~") and normalise(f) not in checks]
        testing = [f for f in frames if held or f.startswith("~")]
        for _ in range(budget if training else 0):
            text = fill(rng.choice(training), rng)
            if normalise(text) not in checks:
                train.append({"text": text, "kind": kind, "family": family})
        tests = {fill(frame, rng) for frame in testing for _ in range(12)}
        held_out.extend({"text": t, "kind": kind, "family": family} for t in sorted(tests) if normalise(t) not in checks)
    return train, held_out


def normalise(text: str) -> str:
    return re.sub(r"[^a-z0-9 ]", "", text.lower()).strip()


# ---------------------------------------------------------------------------------------------------------------------

def download(repo: str, revision: str, path: str, cache: Path, kind: str = "datasets") -> Path:
    target = cache / repo.replace("/", "__") / revision / path
    if not target.exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        base = "https://huggingface.co/datasets" if kind == "datasets" else "https://huggingface.co"
        print(f"Downloading {repo}/{path} ...")
        with urllib.request.urlopen(f"{base}/{repo}/resolve/{revision}/{path}", timeout=600) as response:
            target.with_suffix(".part").write_bytes(response.read())
        target.with_suffix(".part").replace(target)
    return target


def civil(path: Path, size: int, rng: np.random.Generator) -> pd.DataFrame:
    """A sample with all the clearly toxic rows it can hold (they're rare), then random ones"""
    frame = pd.read_parquet(path)
    toxic = frame[frame["toxicity"] >= 0.5]
    toxic = toxic.sample(min(len(toxic), size // 3), random_state=int(rng.integers(1 << 31)))
    rest = frame.drop(toxic.index).sample(size - len(toxic), random_state=int(rng.integers(1 << 31)))
    return pd.concat([toxic, rest]).reset_index(drop=True)


def batches(texts: list[str], size: int):
    for i in range(0, len(texts), size):
        yield texts[i:i + size]


@torch.no_grad()
def predict(model, tokenizer, texts: list[str], device: str, batch_size: int = 64) -> np.ndarray:
    model.eval()
    out = []
    for chunk in batches(texts, batch_size):
        encoded = tokenizer(chunk, truncation=True, max_length=MAX_TOKENS, padding=True, return_tensors="pt").to(device)
        out.append(torch.sigmoid(model(**encoded).logits).float().cpu().numpy())
    return np.concatenate(out)


def predict_onnx(session: ort.InferenceSession, tokenizer, texts: list[str]) -> np.ndarray:
    # One at a time and unpadded, as the API runs it
    out = []
    for text in texts:
        encoded = tokenizer(text, truncation=True, max_length=MAX_TOKENS, return_tensors="np")
        logits = session.run(None, {"input_ids": encoded["input_ids"].astype(np.int64), "attention_mask": encoded["attention_mask"].astype(np.int64)})[0]
        out.append(1 / (1 + np.exp(-logits[0])))
    return np.stack(out)


def targets(kind: str, teacher: np.ndarray) -> np.ndarray:
    """The original model's scores, with the parts this sentence is meant to teach replaced"""
    y = teacher.copy()
    if kind in ("fine", "health"):
        y[:7] = 0
    elif kind == "swearing":
        for label in ("severe_toxicity", "identity_attack", "insult", "threat", "sexual_explicit"):
            y[COL[label]] = 0
    elif kind == "threat":
        y[COL["toxicity"]] = y[COL["threat"]] = 1
    elif kind == "insult":
        y[COL["toxicity"]] = y[COL["insult"]] = 1
    elif kind == "hate":
        y[COL["toxicity"]] = y[COL["identity_attack"]] = 1
    return y


def stopped(scores: np.ndarray) -> np.ndarray:
    return np.any([scores[:, COL[label]] >= limit for label, limit in LIMITS.items()], axis=0)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--blob-root", default=str(REPO / ".blobs"), help="LocalBlobStorage's folder (the API's BlobStorage:Local:Root)")
    parser.add_argument("--s3-bucket", help="Write to this S3 bucket instead (BlobStorage:S3:Bucket); credentials from the usual AWS chain")
    parser.add_argument("--s3-endpoint", help="For S3-compatible storage, e.g. http://localhost:4566 for LocalStack")
    parser.add_argument("--cache", default=str(Path.home() / ".cache/feedfabric"), help="Where downloads are kept between runs")
    parser.add_argument("--civil-rows", type=int, default=24000, help="Civil Comments rows to distil on")
    parser.add_argument("--epochs", type=int, default=2)
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--dry-run", action="store_true", help="Train and test, but don't publish")
    parser.add_argument("--publish-saved", metavar="FOLDER", help="Publish a model an earlier run saved, without training")
    args = parser.parse_args()

    if args.publish_saved:
        publish_folder(args, Path(args.publish_saved))
        return

    started = time.time()
    random.seed(args.seed)
    np_rng = np.random.default_rng(args.seed)
    torch.manual_seed(args.seed)
    device = "mps" if torch.backends.mps.is_available() else "cuda" if torch.cuda.is_available() else "cpu"
    cache = Path(args.cache)

    tokenizer = AutoTokenizer.from_pretrained(BASE_REPO, revision=BASE_REVISION)
    teacher = AutoModelForSequenceClassification.from_pretrained(BASE_REPO, revision=BASE_REVISION).to(device)
    assert [teacher.config.id2label[i] for i in range(len(LABELS))] == LABELS, "The base model's labels changed"

    # Data: Civil Comments to keep what the model knows, the simulator's posts as this app's everyday wording,
    # and the generated sentences
    civil_train = civil(download(DATA_REPO, DATA_REVISION, "data/validation-00000-of-00001.parquet", cache), args.civil_rows, np_rng)
    civil_test = civil(download(DATA_REPO, DATA_REVISION, "data/test-00000-of-00001.parquet", cache), 8000, np_rng)
    posts = pd.read_csv(POSTS, sep="\t", header=None, names=["persona", "text"], quoting=3)["text"].dropna().tolist()
    train_gen, held_out = generated(random.Random(args.seed))
    print(f"Data: {len(civil_train)} Civil Comments, {len(posts)} simulator posts, {len(train_gen)} generated "
          f"({len(held_out)} more held out), on {device}")

    train_texts = civil_train["text"].tolist() + posts + [g["text"] for g in train_gen]
    kinds = ["distil"] * (len(civil_train) + len(posts)) + [g["kind"] for g in train_gen]
    print("Scoring the training data with the original model ...")
    teacher_scores = predict(teacher, tokenizer, train_texts, device)
    y = np.stack([targets(kind, scores) for kind, scores in zip(kinds, teacher_scores)]).astype(np.float32)
    base_civil = predict(teacher, tokenizer, civil_test["text"].tolist(), device)
    base_held = stopped(predict(teacher, tokenizer, [h["text"] for h in held_out], device))

    # Train the teacher's copy in place: it starts as the original model
    model = teacher
    model.train()
    optimiser = torch.optim.AdamW(model.parameters(), lr=1e-5, weight_decay=0.01)
    steps = args.epochs * ((len(train_texts) + 15) // 16)
    schedule = torch.optim.lr_scheduler.OneCycleLR(optimiser, max_lr=1e-5, total_steps=steps, pct_start=0.06, anneal_strategy="linear")
    loss_fn = torch.nn.BCEWithLogitsLoss()
    step = 0
    for epoch in range(args.epochs):
        order = np_rng.permutation(len(train_texts))
        for i in range(0, len(order), 16):
            idx = order[i:i + 16]
            encoded = tokenizer([train_texts[j] for j in idx], truncation=True, max_length=MAX_TOKENS, padding=True, return_tensors="pt").to(device)
            loss = loss_fn(model(**encoded).logits, torch.from_numpy(y[idx]).to(device))
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            optimiser.step()
            schedule.step()
            optimiser.zero_grad()
            step += 1
            if step % 500 == 0:
                print(f"  epoch {epoch + 1}, step {step}/{steps}, loss {loss.item():.4f}, {time.time() - started:.0f}s")
    model.eval()

    # Export as the API runs it: ONNX, weights in 8 bits
    with tempfile.TemporaryDirectory() as tmp:
        full, small = Path(tmp) / "model.onnx", Path(tmp) / "model_quantized.onnx"
        model.to("cpu")
        sample = tokenizer(["Export me"], return_tensors="pt")
        torch.onnx.export(
            model, (sample["input_ids"], sample["attention_mask"]), str(full), dynamo=False, opset_version=17,
            input_names=["input_ids", "attention_mask"], output_names=["logits"],
            dynamic_axes={"input_ids": {0: "batch", 1: "tokens"}, "attention_mask": {0: "batch", 1: "tokens"}, "logits": {0: "batch"}})
        quantize_dynamic(str(full), str(small), weight_type=QuantType.QInt8)
        onnx_bytes = small.read_bytes()
    session = ort.InferenceSession(onnx_bytes, providers=["CPUExecutionProvider"])

    # Tests, on the exported model
    report, failures = {}, []
    tuned_civil = predict_onnx(session, tokenizer, civil_test["text"].tolist())
    report["civilCommentsAuc"] = {}
    for label in OFFENCES:
        truth = civil_test[label].to_numpy() >= 0.5
        if truth.sum() < 10:
            continue
        base_auc, tuned_auc = roc_auc_score(truth, base_civil[:, COL[label]]), roc_auc_score(truth, tuned_civil[:, COL[label]])
        report["civilCommentsAuc"][label] = {"original": round(base_auc, 4), "tuned": round(tuned_auc, 4)}
        if tuned_auc < base_auc - MAX_AUC_DROP:
            failures.append(f"Civil Comments {label} AUC fell from {base_auc:.4f} to {tuned_auc:.4f}")

    held_scores = predict_onnx(session, tokenizer, [h["text"] for h in held_out])
    held_stopped = stopped(held_scores)
    fine = np.array([h["kind"] in ("fine", "swearing", "health") for h in held_out])
    false_flags, base_false_flags = float(held_stopped[fine].mean()), float(base_held[fine].mean())
    recall, base_recall = float(held_stopped[~fine].mean()), float(base_held[~fine].mean())
    report["heldOut"] = {
        "fine": int(fine.sum()), "offensive": int((~fine).sum()),
        "falseFlagRate": {"original": round(base_false_flags, 4), "tuned": round(false_flags, 4)},
        "recall": {"original": round(base_recall, 4), "tuned": round(recall, 4)},
    }
    if false_flags > max(base_false_flags, MAX_FALSE_FLAG_RATE):
        failures.append(f"Stopped {false_flags:.1%} of held-out fine sentences (original {base_false_flags:.1%}, limit {MAX_FALSE_FLAG_RATE:.0%})")
    if recall < base_recall:
        failures.append(f"Caught {recall:.1%} of held-out offensive sentences, fewer than the original's {base_recall:.1%}")
    for h, s, flagged in zip(held_out, held_scores, held_stopped):
        if flagged == (h["kind"] in ("fine", "swearing", "health")):
            print(f"  held out, {'stopped' if flagged else 'missed'}: {h['text']}  " + top(s))

    check_scores = predict_onnx(session, tokenizer, [text for text, _ in CHECKS])
    check_stopped = stopped(check_scores)
    report["checks"] = {"passed": int(sum(bool(s) == want for (_, want), s in zip(CHECKS, check_stopped))), "total": len(CHECKS)}
    print("\nChecks (stop = the API would refuse it):")
    for (text, want), s, got in zip(CHECKS, check_scores, check_stopped):
        mark = "ok  " if bool(got) == want else "FAIL"
        print(f"  {mark} {'stop' if got else 'pass'}  {text[:60]:<60}  {top(s)}")
        if bool(got) != want:
            failures.append(f"Check {'not stopped' if want else 'stopped'}: {text}")

    print("\n" + json.dumps(report, indent=2))
    saved = save(cache, onnx_bytes, report)
    if failures:
        raise SystemExit("Not published:\n  " + "\n  ".join(failures) + f"\nThe model is in {saved}, for a closer look")
    if args.dry_run:
        print(f"Dry run: not published. To publish this model: uv run --group tune tune_toxicity.py --publish-saved {saved}")
        return
    publish_folder(args, saved)
    print(f"Published {PREFIX} in {time.time() - started:.0f}s; restart the API to load it.")


def save(cache: Path, onnx_bytes: bytes, report: dict) -> Path:
    """The model and everything needed to publish it, in a folder of its own"""
    folder = cache / "tuned-toxicity" / time.strftime("%Y%m%d-%H%M%S")
    folder.mkdir(parents=True)
    (folder / "model.onnx").write_bytes(onnx_bytes)
    (folder / "report.json").write_text(json.dumps({"tunedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "tests": report}, indent=2))
    return folder


def publish_folder(args: argparse.Namespace, folder: Path) -> None:
    saved = json.loads((folder / "report.json").read_text())
    cache = Path(args.cache)
    files = {
        f"{PREFIX}/model.onnx": (folder / "model.onnx").read_bytes(),
        f"{PREFIX}/vocab.json": download(BASE_REPO, BASE_REVISION, "vocab.json", cache, kind="models").read_bytes(),
        f"{PREFIX}/merges.txt": download(BASE_REPO, BASE_REVISION, "merges.txt", cache, kind="models").read_bytes(),
    }
    meta = {
        "kind": "text-classifier",
        "tokenizer": "roberta-bpe",
        "multiLabel": True,
        "maxTokens": MAX_TOKENS,
        "labels": LABELS,
        "source": f"{BASE_REPO} (Apache 2.0), fine-tuned on Civil Comments (CC0) and FeedFabric's own wording by ml/tune_toxicity.py",
        "sources": [{"repo": BASE_REPO, "revision": BASE_REVISION}, {"repo": DATA_REPO, "revision": DATA_REVISION}],
        "tunedAt": saved["tunedAt"],
        "tests": saved["tests"],
        "sha256": {key.rsplit("/", 1)[1]: hashlib.sha256(data).hexdigest() for key, data in files.items()},
    }
    # meta.json last, so the API never reads a meta.json for files that aren't there yet
    files[f"{PREFIX}/meta.json"] = json.dumps(meta, indent=2).encode()
    for key, data in files.items():
        publish(args, key, data)
    print(f"Published {PREFIX} from {folder} to {args.s3_bucket or args.blob_root}")


def top(scores: np.ndarray) -> str:
    return ", ".join(f"{label} {scores[COL[label]]:.2f}" for label in OFFENCES if scores[COL[label]] >= 0.3) or "all low"


def publish(args: argparse.Namespace, key: str, data: bytes) -> None:
    content_type = "application/json" if key.endswith(".json") else "text/plain" if key.endswith(".txt") else "application/octet-stream"
    if args.s3_bucket:
        import boto3

        boto3.client("s3", endpoint_url=args.s3_endpoint).upload_fileobj(
            io.BytesIO(data), args.s3_bucket, key, ExtraArgs={"ContentType": content_type})
        return
    path = Path(args.blob_root) / key
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_bytes(data)
    temp.replace(path)
    # LocalBlobStorage keeps each file's content type beside it
    Path(f"{path}.content-type").write_text(content_type)


if __name__ == "__main__":
    main()
