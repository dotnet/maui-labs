"""Extract synthetic NLEmbedding device observations and compare per-query ranks."""

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def collect(path):
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(path).getroot()
    results = [
        result for result in root.findall(".//t:UnitTestResult", namespace)
        if "NLEmbeddingChunkingExperimentTests." in result.get("testName", "")
    ]
    if len(results) != 1:
        raise ValueError("Expected exactly one complete chunking experiment test.")
    result = results[0]
    if result.get("outcome") != "Passed":
        raise ValueError("The chunking experiment test did not pass.")
    output = result.findtext("t:Output/t:StdOut", default="", namespaces=namespace)
    observations = [
        json.loads(line.removeprefix("APPLE_CHUNKING "))
        for line in output.splitlines() if line.startswith("APPLE_CHUNKING ")
    ]
    policies = {
        "whole", "fixed120", "fixed180", "fixed240", "fixed360", "fixed720",
        "fixed180_overlap45", "fixed360_overlap90", "fixed720_overlap180",
        "sentence1", "sentence2", "sentence3", "sentence2_overlap1", "sentence3_overlap1",
    }
    scenarios = {"short", "matched_early", "matched_middle", "matched_late", "playground"}
    expected = {(trial, policy, scenario)
                for trial in (1, 2, 3) for policy in policies for scenario in scenarios}
    actual = {(o["Trial"], o["Policy"], o["Scenario"]) for o in observations}
    if actual != expected or len(observations) != len(expected):
        raise ValueError("Missing, duplicate, or unexpected experiment observations.")
    identities = {
        json.dumps({key: o[key] for key in
                    ("SchemaVersion", "CorpusFingerprint", "Platform", "OSVersion", "Model")},
                   sort_keys=True) for o in observations
    }
    if len(identities) != 1:
        raise ValueError("Mixed model, OS, corpus, or protocol identities.")
    return observations


def rows(observations):
    result = {}
    for observation in observations:
        for query in observation["Observations"]:
            key = (observation["Trial"], observation["Policy"],
                   observation["Scenario"], query["Id"])
            if key in result:
                raise ValueError("Duplicate query observation.")
            result[key] = query
    return result


def compare(current, baseline):
    identity = ("SchemaVersion", "CorpusFingerprint", "Platform", "OSVersion", "Model")
    for key in identity:
        if current[0][key] != baseline[0][key]:
            raise ValueError(f"Incompatible {key}; re-evaluate instead of replacing a baseline silently.")
    actual, expected = rows(current), rows(baseline)
    if actual.keys() != expected.keys():
        raise ValueError("Incompatible query sets.")
    failures = []
    for key, previous in expected.items():
        now = actual[key]
        ranks = {entry["DocumentId"]: entry["Rank"] for entry in now["RelevantRanks"]}
        for entry in previous["RelevantRanks"]:
            rank = ranks.get(entry["DocumentId"])
            if rank is None or rank > entry["Rank"]:
                failures.append(f"{key}: {entry['DocumentId']} rank {entry['Rank']} -> {rank}")
        old_rank, new_rank = previous["EvidenceRank"], now["EvidenceRank"]
        if old_rank is not None and (new_rank is None or new_rank > old_rank):
            failures.append(f"{key}: evidence rank {old_rank} -> {new_rank}")
    return failures


def summary(observations):
    print("| Policy | Playground doc@1 | Evidence@1 | Evidence@3 | Evidence@5 | Chunks | Matched doc@1 |")
    print("|---|---:|---:|---:|---:|---:|---:|")
    for observation in observations:
        if observation["Trial"] != 1 or observation["Scenario"] != "playground":
            continue
        queries = observation["Observations"]
        count = len(queries)
        evidence = [q["EvidenceRank"] for q in queries]
        matched = [q for o in observations if o["Trial"] == 1
                   and o["Policy"] == observation["Policy"] and o["Scenario"].startswith("matched_")
                   for q in o["Observations"]]
        doc1 = sum(q["RelevantRanks"][0]["Rank"] == 1 for q in queries)
        matched1 = sum(q["RelevantRanks"][0]["Rank"] == 1 for q in matched)
        cells = [f"{sum(rank is not None and rank <= k for rank in evidence)}/{count}"
                 for k in (1, 3, 5)]
        print(f"| {observation['Policy']} | {doc1}/{count} | {' | '.join(cells)} | "
              f"{observation['Chunks']} | {matched1}/{len(matched)} |")


def candidate_baseline(observations):
    first = observations[0]
    model = first["Model"]
    return {
        "SchemaVersion": 1,
        "CorpusFingerprint": first["CorpusFingerprint"],
        "ModelKey": f"{model['Kind']}:{model['ConfiguredLanguage']}:{model['Revision']}:{model['Dimensions']}",
        "ObservedPlatform": first["Platform"],
        "ObservedOSVersion": first["OSVersion"],
        "Expectations": {
            f"{observation['Policy']}/{observation['Scenario']}/{query['Id']}": {
                "DocumentRanks": {entry["DocumentId"]: entry["Rank"] for entry in query["RelevantRanks"]},
                "EvidenceRank": query["EvidenceRank"],
            }
            for observation in observations if observation["Trial"] == 1
            for query in observation["Observations"]
        },
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--compare", type=Path)
    parser.add_argument("--baseline-output", type=Path,
                        help="Write a new candidate device rank baseline; refuses to overwrite an existing file.")
    args = parser.parse_args()
    if args.output.resolve() in {args.trx.resolve(), args.compare.resolve() if args.compare else None}:
        parser.error("Output must not overwrite a TRX or comparison baseline.")
    try:
        observations = collect(args.trx)
        result = {
            "schemaVersion": 1,
            "scope": "Synthetic native device experiments, not universal optimal sizes or quality guarantees.",
            "observations": observations,
        }
        args.output.write_text(json.dumps(result, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
        summary(observations)
        if args.baseline_output:
            with args.baseline_output.open("x", encoding="utf-8") as destination:
                json.dump(candidate_baseline(observations), destination, indent=2)
                destination.write("\n")
            print("Wrote a candidate rank baseline. Review it before accepting it; observed failures are not quality goals.")
        if args.compare:
            baseline = json.loads(args.compare.read_text(encoding="utf-8"))
            failures = compare(observations, baseline["observations"])
            for failure in failures:
                print(f"REGRESSION: {failure}", file=sys.stderr)
            if failures:
                return 2
            print("No per-query document/evidence rank regression against the compatible baseline.")
        print("Missing evidence in the baseline remains a known limitation, not an acceptable-answer claim.")
        return 0
    except (ValueError, KeyError, OSError, ET.ParseError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
