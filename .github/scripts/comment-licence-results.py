"""Post the local ORT result on its pull request."""

import html
import json
import os
from pathlib import Path
from urllib.request import Request, urlopen


MARKER = "<!-- ort-licence-compliance -->"
API = "https://api.github.com"


def api(path, method="GET", body=None):
    data = json.dumps(body).encode() if body is not None else None
    request = Request(
        API + path,
        data=data,
        method=method,
        headers={
            "Authorization": f"Bearer {os.environ['GH_TOKEN']}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "Content-Type": "application/json",
        },
    )
    with urlopen(request) as response:
        return json.load(response)


def cell(value):
    content = html.escape(str(value).replace("|", "\\|").replace("\n", " ")[:300])
    return f"<code>{content}</code>"


def failure_body(evaluation, run_url):
    lines = [MARKER, "## Licence compliance failed", ""]
    if evaluation.exists():
        try:
            violations = json.loads(evaluation.read_text())["evaluator"]["violations"]
        except (ValueError, KeyError, TypeError):
            violations = None
        if violations is None:
            lines.append("ORT produced an unreadable evaluation result. Check the workflow log.")
        elif violations:
            lines.extend(["| Package | Licence | Finding |", "| --- | --- | --- |"])
            for violation in violations[:20]:
                lines.append(
                    f"| {cell(violation['pkg'])} | {cell(violation.get('license') or 'Unknown')} "
                    f"| {cell(violation['message'])} |"
                )
            if len(violations) > 20:
                lines.append(f"\n{len(violations) - 20} more findings are in the workflow result.")
        else:
            lines.append("ORT failed without a policy violation. Check the workflow log.")
    else:
        lines.append("ORT did not produce an evaluation result. Check the workflow log.")
    lines.extend(["", f"[View the ORT run]({run_url})"])
    return "\n".join(lines)


def main():
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    repo = os.environ["GITHUB_REPOSITORY"]
    number = event["number"]
    pr = api(f"/repos/{repo}/pulls/{number}")
    if pr["head"]["sha"] != event["pull_request"]["head"]["sha"]:
        return

    existing = None
    page = 1
    while True:
        comments = api(f"/repos/{repo}/issues/{number}/comments?per_page=100&page={page}")
        for comment in comments:
            if comment["user"]["login"] == "github-actions[bot]" and MARKER in comment["body"]:
                existing = comment
                break
        if existing or len(comments) < 100:
            break
        page += 1

    if os.environ["ORT_SUCCEEDED"] != "true":
        run_url = f"https://github.com/{repo}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
        evaluation = Path(os.environ["RUNNER_TEMP"]) / "ort-results/evaluation-result.json"
        body = failure_body(evaluation, run_url)
    elif existing:
        body = f"{MARKER}\n## Licence compliance passed\n\nThe current ORT run passed."
    else:
        return

    if existing:
        api(f"/repos/{repo}/issues/comments/{existing['id']}", "PATCH", {"body": body})
    else:
        api(f"/repos/{repo}/issues/{number}/comments", "POST", {"body": body})


if __name__ == "__main__":
    main()
