import argparse
import csv
import json
import os
import pickle
from pathlib import Path

from sklearn.feature_extraction import DictVectorizer
from sklearn.metrics import accuracy_score, f1_score, precision_score, recall_score, roc_auc_score
from sklearn.model_selection import train_test_split
from xgboost import XGBClassifier


TARGET_COLUMNS = {
    "label_alarm_in_next_30m",
    "label_batch_failure",
    "label_service_intervention_24h",
}

NUMERIC_COLUMNS = {
    "risk_score",
    "health_score",
    "warn_count",
    "alarm_count",
    "total_events",
    "alert_pressure",
    "recent_alarm_events",
    "recent_warn_events",
    "fleet_pressure",
    "threshold_reached",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train a baseline XGBoost model for PathoNet prediction data.")
    parser.add_argument("--input-csv", required=True, help="Path to exported CSV dataset.")
    parser.add_argument("--target", default="label_alarm_in_next_30m", choices=sorted(TARGET_COLUMNS))
    parser.add_argument("--output-dir", default="tmp/ml/model-output")
    parser.add_argument("--test-size", type=float, default=0.25)
    return parser.parse_args()


def load_rows(csv_path: Path, target_column: str):
    features: list[dict[str, object]] = []
    labels: list[int] = []

    with csv_path.open("r", encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            target_value = row.get(target_column, "")
            if target_value not in {"0", "1"}:
                continue

            feature_row: dict[str, object] = {}
            for key, value in row.items():
                if key == target_column or key in TARGET_COLUMNS:
                    continue

                if key in NUMERIC_COLUMNS:
                    feature_row[key] = float(value or 0)
                else:
                    feature_row[key] = value or ""

            features.append(feature_row)
            labels.append(int(target_value))

    if not features:
        raise ValueError("Dataset does not contain any resolved rows for the selected target.")

    return features, labels


def main() -> None:
    args = parse_args()
    input_csv = Path(args.input_csv).resolve()
    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    features, labels = load_rows(input_csv, args.target)

    vectorizer = DictVectorizer(sparse=True)
    x_all = vectorizer.fit_transform(features)

    x_train, x_test, y_train, y_test = train_test_split(
        x_all,
        labels,
        test_size=args.test_size,
        random_state=42,
        stratify=labels if len(set(labels)) > 1 else None,
    )

    model = XGBClassifier(
        n_estimators=160,
        max_depth=5,
        learning_rate=0.08,
        subsample=0.9,
        colsample_bytree=0.9,
        objective="binary:logistic",
        eval_metric="logloss",
        random_state=42,
    )

    model.fit(x_train, y_train)

    y_pred = model.predict(x_test)
    y_proba = model.predict_proba(x_test)[:, 1]

    metrics = {
        "target": args.target,
        "input_csv": str(input_csv),
        "row_count": len(labels),
        "train_size": len(y_train),
        "test_size": len(y_test),
        "positive_ratio": sum(labels) / len(labels),
        "accuracy": accuracy_score(y_test, y_pred),
        "precision": precision_score(y_test, y_pred, zero_division=0),
        "recall": recall_score(y_test, y_pred, zero_division=0),
        "f1": f1_score(y_test, y_pred, zero_division=0),
        "roc_auc": roc_auc_score(y_test, y_proba) if len(set(y_test)) > 1 else None,
        "feature_count": len(vectorizer.feature_names_),
    }

    model_path = output_dir / f"{args.target}-xgboost-model.json"
    vectorizer_path = output_dir / f"{args.target}-vectorizer.pkl"
    metrics_path = output_dir / f"{args.target}-metrics.json"

    model.save_model(str(model_path))
    with vectorizer_path.open("wb") as handle:
        pickle.dump(vectorizer, handle)
    metrics_path.write_text(json.dumps(metrics, indent=2), encoding="utf-8")

    print(json.dumps({
        "model_path": str(model_path),
        "vectorizer_path": str(vectorizer_path),
        "metrics_path": str(metrics_path),
        "metrics": metrics,
    }, indent=2))


if __name__ == "__main__":
    main()
