"""Downloads the pretrained models the app runs with ONNX Runtime and publishes them to blob storage.

    uv run fetch_models.py                       # to ../.blobs (LocalBlobStorage in dev)
    uv run fetch_models.py --s3-bucket my-bucket # or to S3, where a server reads them

Both are pinned to a revision, so a build always gets the same model:
  models/safety/nsfw/       nudity classifier (ViT, 224x224): onnx-community/nsfw_image_detection-ONNX, an ONNX
                            export of Falconsai/nsfw_image_detection (Apache 2.0); the 8-bit quantised file, for CPUs
  models/safety/toxicity/   offensive-language classifier (RoBERTa, 16 labels): unitary/unbiased-toxic-roberta, Detoxify's
                            "unbiased" model (Apache 2.0), ONNX export by onnx-community; the 8-bit file
  models/embeddings/minilm/ sentence embeddings (384 dimensions): sentence-transformers/all-MiniLM-L6-v2 (Apache 2.0)
Each folder gets a meta.json with what the app needs to use it (input size, normalisation, labels, vocabulary).
"""

import argparse
import hashlib
import json
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

MODELS = [
    {
        "prefix": "models/safety/nsfw",
        "repo": "onnx-community/nsfw_image_detection-ONNX",
        "revision": "1ceb3c7fe1e9f3f2507e6df577437f23a9149fd5",
        "files": {"model.onnx": "onnx/model_quantized.onnx"},
        "meta": {
            "kind": "image-classifier",
            "imageSize": 224,
            "mean": [0.5, 0.5, 0.5],
            "std": [0.5, 0.5, 0.5],
            "labels": ["normal", "nsfw"],
            "source": "Falconsai/nsfw_image_detection (Apache 2.0), ONNX export by onnx-community",
        },
    },
    {
        # Detoxify's "unbiased" model: trained on the Jigsaw Unintended Bias (Civil Comments) and Toxic Comment data,
        # so mentioning who someone is ("as a gay man...") isn't scored as an attack the way toxic-bert scores it
        "prefix": "models/safety/toxicity",
        "repo": "onnx-community/unbiased-toxic-roberta-ONNX",
        "revision": "d1d71b6aa711966a0dba4027b4fcd41c878c8cd5",
        "files": {"model.onnx": "onnx/model_quantized.onnx", "vocab.json": "vocab.json", "merges.txt": "merges.txt"},
        "meta": {
            "kind": "text-classifier",
            "tokenizer": "roberta-bpe",
            "multiLabel": True,
            "maxTokens": 256,
            # Offence labels, then the identities a text mentions (not offences in themselves)
            "labels": [
                "toxicity", "severe_toxicity", "obscene", "identity_attack", "insult", "threat", "sexual_explicit",
                "male", "female", "homosexual_gay_or_lesbian", "christian", "jewish", "muslim", "black", "white",
                "psychiatric_or_mental_illness",
            ],
            "source": "unitary/unbiased-toxic-roberta (Apache 2.0), ONNX export by onnx-community",
        },
    },
    {
        "prefix": "models/embeddings/minilm",
        "repo": "sentence-transformers/all-MiniLM-L6-v2",
        "revision": "1110a243fdf4706b3f48f1d95db1a4f5529b4d41",
        "files": {"model.onnx": "onnx/model.onnx", "vocab.txt": "vocab.txt"},
        "meta": {
            "kind": "text-embedding",
            "dimensions": 384,
            "maxTokens": 128,
            "pooling": "mean",
            "normalize": True,
            "lowercase": True,
            "source": "sentence-transformers/all-MiniLM-L6-v2 (Apache 2.0)",
        },
    },
]


def download(repo: str, revision: str, path: str) -> bytes:
    url = f"https://huggingface.co/{repo}/resolve/{revision}/{path}"
    with urllib.request.urlopen(url, timeout=300) as response:
        return response.read()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--blob-root", default=str(REPO / ".blobs"), help="LocalBlobStorage's folder (the API's BlobStorage:Local:Root)")
    parser.add_argument("--s3-bucket", help="Write to this S3 bucket instead (BlobStorage:S3:Bucket)")
    parser.add_argument("--s3-endpoint", help="For S3-compatible storage, e.g. http://localhost:4566 for LocalStack")
    args = parser.parse_args()

    for model in MODELS:
        # A model is one repo, or several parts (the ONNX export from one repo, its vocabulary from another)
        parts = model.get("parts") or [{"repo": model["repo"], "revision": model["revision"], "files": model["files"]}]
        files = {}
        for part in parts:
            for name, path in part["files"].items():
                print(f"Downloading {part['repo']}/{path} ...")
                files[name] = download(part["repo"], part["revision"], path)
        meta = {
            **model["meta"],
            "sources": [{"repo": p["repo"], "revision": p["revision"]} for p in parts],
            "sha256": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()},
        }
        files["meta.json"] = json.dumps(meta, indent=2).encode()
        for name, data in files.items():
            publish(args, f"{model['prefix']}/{name}", data)
        print(f"Published {model['prefix']} ({sum(len(d) for d in files.values()) / 1e6:.0f} MB)")


def publish(args: argparse.Namespace, key: str, data: bytes) -> None:
    content_type = "application/json" if key.endswith(".json") else "text/plain" if key.endswith(".txt") else "application/octet-stream"
    if args.s3_bucket:
        import io

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
