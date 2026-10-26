import argparse
import bisect
import datetime as dt
import glob
import json
import os
import re
import sys
from collections import defaultdict

ATTENTION = {"Question", "Permission", "Error"}
AFTER_PROMPT = {"Working", "Permission"}
WAKE_KINDS = {"task-notification", "peer"}
WRAP_UP = "mcp__ccd_turn__wrap_up"
AUTO_RESUME = ("Your response above was cut off", "[Your previous response")
BACKGROUND = re.compile(r"bg=[1-9]")
SETTLE = dt.timedelta(seconds=5)
EARLY = dt.timedelta(seconds=1)
MAX_EXAMPLES = 25
LOCAL = dt.datetime.now().astimezone().tzinfo

LOG_EVENT = re.compile(r"^(\S+ \S+)  claude: (\S+) (.*?)session=(\w{8}) .*session:(\w+) -> \w+")


def parse_args():
    parser = argparse.ArgumentParser(description="Сверка статусов AgentGlow с транскриптами Claude Code")
    parser.add_argument("--days", type=int, default=7)
    parser.add_argument("--data", default=os.path.join(os.environ.get("LOCALAPPDATA", ""), "AgentGlow"))
    config = os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(os.path.expanduser("~"), ".claude")
    parser.add_argument("--projects", default=os.path.join(config, "projects"))
    parser.add_argument("--out")
    return parser.parse_args()


def utc(text):
    return dt.datetime.fromisoformat(text.replace("Z", "+00:00"))


def read_lines(path):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8", errors="replace") as handle:
        return handle.read().splitlines()


class Actual:
    def __init__(self):
        self.timeline = defaultdict(list)
        self.events = defaultdict(list)
        self.prompts = defaultdict(list)
        self.downtime = []
        self.errors = []
        self.timeouts = []
        self.history_start = None
        self.log_start = None

    def add(self, key, when, status, cause):
        self.timeline[key].append((when, status, cause))

    def finish(self):
        for entries in self.timeline.values():
            entries.sort(key=lambda entry: entry[0])
        for entries in self.prompts.values():
            entries.sort()

    def status_at(self, key, when):
        entries = self.timeline.get(key)
        if not entries:
            return None
        index = bisect.bisect_right([entry[0] for entry in entries], when) - 1
        return entries[index][1] if index >= 0 else None

    def stop_near(self, key, start, end):
        middle = start + (end - start) / 2
        stops = [entry for entry in self.timeline.get(key, []) + self.events.get(key, [])
                 if start <= entry[0] <= end and entry[-1].startswith("claude Stop ")]
        if not stops:
            return None
        entry = min(stops, key=lambda item: abs(item[0] - middle))
        status = entry[1] if len(entry) == 3 else self.status_at(key, entry[0])
        return status, entry[-1]

    def statuses_between(self, key, start, end):
        statuses = [self.status_at(key, start)]
        for when, status, cause in self.timeline.get(key, []):
            if start < when <= end:
                statuses.append(status)
        return statuses

    def known(self, key, begin, end):
        start = self.history_start if self.history_start and begin >= self.history_start else self.log_start
        if start is None or begin < start:
            return False
        if key not in self.timeline and start != self.history_start:
            return False
        return not any(stopped <= end and begin <= started for stopped, started in self.downtime)


def load_actual(data_dir, since):
    actual = Actual()
    history = []
    for name in ("status-history.old.txt", "status-history.txt"):
        for line in read_lines(os.path.join(data_dir, name)):
            parts = line.split("\t")
            if len(parts) != 6:
                continue
            try:
                when = dt.datetime.fromisoformat(parts[0])
            except ValueError:
                continue
            history.append((when, parts))
    history.sort(key=lambda item: item[0])
    if history:
        actual.history_start = history[0][0]
    last_seen = None
    parked = False
    last_cause = {}
    for when, (_, kind, key, before, after, cause) in history:
        if kind == "mark" and cause.startswith("start "):
            actual.downtime.append((last_seen or when, when))
        last_seen = when
        parked = kind == "mark" and cause.startswith("parked:")
        if kind == "mark":
            continue
        if kind == "event":
            actual.events[key].append((when, cause))
            if cause.startswith("claude UserPromptSubmit"):
                actual.prompts[key].append((when, cause.endswith(" wake")))
            continue
        if kind != "thread":
            continue
        actual.add(key, when, after, cause)
        restored = last_cause.get(key) == "start"
        last_cause[key] = cause
        if when < since or cause == "start" or "/" in key:
            continue
        if after == "Error":
            actual.errors.append((when, key, cause))
        if cause == "housekeeping" and before == "Working" and after != "gone" and not restored:
            actual.timeouts.append((when, key, after))
    if parked:
        actual.downtime.append((last_seen, dt.datetime.max.replace(tzinfo=LOCAL)))
    for name in ("log.old.txt", "log.txt"):
        for line in read_lines(os.path.join(data_dir, name)):
            match = LOG_EVENT.match(line)
            if not match:
                continue
            when = dt.datetime.strptime(match.group(1), "%Y-%m-%d %H:%M:%S").replace(tzinfo=LOCAL)
            if actual.log_start is None:
                actual.log_start = when
            if when < since or (actual.history_start and when >= actual.history_start):
                continue
            event, detail, key, status = match.group(2), match.group(3).strip(), match.group(4), match.group(5)
            actual.add(key, when, status, "claude " + event + " " + detail)
            if event == "UserPromptSubmit":
                actual.prompts[key].append((when, detail.startswith("wake")))
            if event == "StopFailure":
                actual.errors.append((when, key, "claude StopFailure"))
    actual.finish()
    return actual


def text_of(content):
    if isinstance(content, str):
        return content
    if not isinstance(content, list):
        return ""
    return " ".join(part.get("text", "") for part in content if isinstance(part, dict) and part.get("type") == "text")


def has_tool_result(content):
    return isinstance(content, list) and any(isinstance(part, dict) and part.get("type") == "tool_result" for part in content)


def asks_user(tool_input):
    if tool_input.get("blocked") is True:
        return "Question/blocked"
    ask = tool_input.get("ask")
    return "Question/ask" if isinstance(ask, str) and ask.strip() != "" else False


def load_transcript(path, since):
    events = []
    questions = {}
    with open(path, encoding="utf-8", errors="replace") as handle:
        for number, line in enumerate(handle, 1):
            try:
                record = json.loads(line)
            except ValueError:
                continue
            if not isinstance(record, dict) or record.get("isSidechain") or "timestamp" not in record:
                continue
            when = utc(record["timestamp"])
            kind = record.get("type")
            if kind == "system":
                subtype = record.get("subtype")
                if subtype in ("stop_hook_summary", "api_error"):
                    events.append((when, subtype, None, number))
            elif kind == "assistant":
                if record.get("isApiErrorMessage"):
                    events.append((when, "api_error", None, number))
                for part in (record.get("message") or {}).get("content") or []:
                    if not isinstance(part, dict) or part.get("type") != "tool_use":
                        continue
                    if part.get("name") == WRAP_UP:
                        events.append((when, "wrap_up", asks_user(part.get("input") or {}), number))
                    elif part.get("name") == "AskUserQuestion":
                        questions[part.get("id")] = (when, number)
            elif kind == "user":
                content = (record.get("message") or {}).get("content")
                if has_tool_result(content):
                    for part in content:
                        if isinstance(part, dict) and part.get("tool_use_id") in questions:
                            start, start_line = questions.pop(part["tool_use_id"])
                            events.append((start, "ask_question", when, start_line))
                    continue
                origin = record.get("origin")
                origin_kind = origin.get("kind") if isinstance(origin, dict) else origin
                text = text_of(content).lstrip()
                if text.startswith("[Request interrupted by user"):
                    events.append((when, "interrupt", None, number))
                elif text.startswith(AUTO_RESUME):
                    events.append((when, "api_error", None, number))
                elif origin_kind == "human":
                    events.append((when, "prompt", "human", number))
                elif origin_kind in WAKE_KINDS:
                    events.append((when, "prompt", "wake", number))
    events.sort(key=lambda event: event[0])
    return [event for event in events if event[0] >= since]


class Report:
    def __init__(self):
        self.checks = {}
        self.order = []

    def check(self, name, title):
        if name not in self.checks:
            self.checks[name] = {"title": title, "total": 0, "ok": 0, "skipped": 0, "notes": defaultdict(int), "bad": []}
            self.order.append(name)
        return self.checks[name]

    def ok(self, name, title):
        entry = self.check(name, title)
        entry["total"] += 1
        entry["ok"] += 1

    def skip(self, name, title):
        self.check(name, title)["skipped"] += 1

    def note(self, name, title, label):
        entry = self.check(name, title)
        entry["total"] += 1
        entry["ok"] += 1
        entry["notes"][label] += 1

    def bad(self, name, title, row):
        entry = self.check(name, title)
        entry["total"] += 1
        entry["bad"].append(row)


def local(when):
    return when.astimezone(LOCAL).strftime("%m-%d %H:%M:%S")


def reconcile_session(report, actual, key, project, path, events):
    awaiting = False
    rel = os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path)
    for when, kind, value, line in events:
        where = rel + ":" + str(line)
        if kind == "wrap_up":
            awaiting = value
        elif kind == "prompt":
            if value == "human":
                awaiting = False
            check_prompt(report, actual, key, project, when, value, where)
        elif kind == "stop_hook_summary":
            check_turn_end(report, actual, key, project, when, awaiting, where)
        elif kind == "ask_question":
            check_question(report, actual, key, project, when, value, where)
        elif kind == "interrupt":
            probe = when + dt.timedelta(minutes=1)
            if actual.known(key, when - EARLY, probe):
                report.note("I", "Прерывание (Esc): статус через минуту", str(actual.status_at(key, probe)))
            else:
                report.skip("I", "Прерывание (Esc): статус через минуту")


def check_turn_end(report, actual, key, project, when, awaiting, where):
    name, title = "T", "Конец хода: «Закончил» или «Задал вопрос»"
    if not actual.known(key, when - SETTLE, when + SETTLE):
        report.skip(name, title)
        return
    expected = awaiting or "Done"
    stop = actual.stop_near(key, when - SETTLE, when + SETTLE)
    if stop is None:
        report.bad(name, title, (when, key, project, expected, "Stop не пришёл", where))
        return
    status, cause = stop
    if awaiting and status == "Question":
        report.ok(name, title)
    elif not awaiting and status in ("Done", "DoneIdle"):
        report.ok(name, title)
    elif status in ("removed", "gone"):
        report.note(name, title, "тред закрыт")
    elif not awaiting and status == "Working" and BACKGROUND.search(cause):
        report.note(name, title, "фоновые задачи")
    elif not awaiting and status in ATTENTION and status != "Question":
        report.note(name, title, "агент держит " + status)
    else:
        report.bad(name, title, (when, key, project, expected, status, where))


def check_prompt(report, actual, key, project, when, kind, where):
    name, title = "P", "Промпт: «Работаю» и признак пробуждения"
    if not actual.known(key, when - EARLY, when + SETTLE):
        report.skip(name, title)
        return
    statuses = actual.statuses_between(key, when - EARLY, when + SETTLE)
    flag = nearest_prompt(actual, key, when)
    expected_wake = kind == "wake"
    if flag is None and expected_wake:
        report.skip(name, title)
    elif flag is None:
        report.bad(name, title, (when, key, project, "UserPromptSubmit", "не пришёл", where))
    elif flag != expected_wake:
        report.bad(name, title, (when, key, project, "wake" if expected_wake else "human", "wake" if flag else "human", where))
    elif kind == "human" and statuses[-1] not in AFTER_PROMPT and not any(status in AFTER_PROMPT for status in statuses[1:]):
        report.bad(name, title, (when, key, project, "Working", statuses[-1], where))
    else:
        report.ok(name, title)


def nearest_prompt(actual, key, when):
    best = None
    for prompt_time, wake in actual.prompts.get(key, []):
        delta = abs((prompt_time - when).total_seconds())
        if delta <= 3 and (best is None or delta < best[0]):
            best = (delta, wake)
    return None if best is None else best[1]


def check_question(report, actual, key, project, start, end, where):
    name, title = "A", "AskUserQuestion: «Задал вопрос», пока нет ответа"
    if (end - start).total_seconds() < 2 * SETTLE.total_seconds():
        report.skip(name, title)
        return
    probe = start + SETTLE
    if not actual.known(key, start - EARLY, probe):
        report.skip(name, title)
        return
    status = actual.status_at(key, probe)
    if status in ATTENTION:
        report.ok(name, title)
    else:
        report.bad(name, title, (start, key, project, "Question", status, where))


def check_errors(report, actual, api_errors):
    name, title = "E", "«Ошибка»: есть ошибка API в транскрипте"
    for when, key, cause in actual.errors:
        near = [moment for moment in api_errors.get(key, []) if -SETTLE <= when - moment <= dt.timedelta(minutes=15)]
        if near:
            report.ok(name, title)
        else:
            report.bad(name, title, (when, key, "", "api_error до ошибки", "нет", cause))


def check_timeouts(report, actual):
    name, title = "W", "«Работаю» снят таймаутом (пропущен конец хода)"
    for when, key, after in actual.timeouts:
        report.bad(name, title, (when, key, "", "Stop до таймаута", after, "housekeeping"))


def render(report, since, actual, sessions):
    lines = ["# Сверка AgentGlow с транскриптами Claude Code", ""]
    lines.append("Период: с " + local(since) + ", тредов с транскриптом: " + str(sessions) + ".")
    source = []
    if actual.history_start:
        source.append("status-history.txt с " + local(actual.history_start))
    if actual.log_start:
        source.append("log.txt с " + local(actual.log_start))
    lines.append("Данные AgentGlow: " + (", ".join(source) if source else "нет") + ".")
    lines.append("")
    lines.append("| Проверка | Сверено | Совпало | Расхождений | Нет данных | Пометки |")
    lines.append("|---|---|---|---|---|---|")
    for name in report.order:
        entry = report.checks[name]
        notes = ", ".join(label + ": " + str(count) for label, count in sorted(entry["notes"].items()))
        lines.append("| " + name + ". " + entry["title"] + " | " + str(entry["total"]) + " | " + str(entry["ok"]) + " | "
                     + str(len(entry["bad"])) + " | " + str(entry["skipped"]) + " | " + notes + " |")
    for name in report.order:
        entry = report.checks[name]
        if not entry["bad"]:
            continue
        lines += ["", "## " + name + ". " + entry["title"], "", "| Время | Тред | Проект | Ожидалось | AgentGlow | Где |", "|---|---|---|---|---|---|"]
        for when, key, project, expected, status, where in sorted(entry["bad"])[-MAX_EXAMPLES:]:
            lines.append("| " + local(when) + " | " + key + " | " + project + " | " + str(expected) + " | " + str(status) + " | " + where + " |")
        if len(entry["bad"]) > MAX_EXAMPLES:
            lines.append("")
            lines.append("Показаны последние " + str(MAX_EXAMPLES) + " из " + str(len(entry["bad"])) + ".")
    return "\n".join(lines) + "\n"


def main():
    args = parse_args()
    since = dt.datetime.now(LOCAL) - dt.timedelta(days=args.days)
    actual = load_actual(args.data, since)
    report = Report()
    api_errors = defaultdict(list)
    sessions = 0
    for path in glob.glob(os.path.join(args.projects, "*", "*.jsonl")):
        if dt.datetime.fromtimestamp(os.path.getmtime(path), LOCAL) < since:
            continue
        session_id = os.path.splitext(os.path.basename(path))[0]
        key = session_id[:8]
        events = load_transcript(path, since)
        if not events:
            continue
        sessions += 1
        project = os.path.basename(os.path.dirname(path)).split("-")[-1]
        api_errors[key] = [when for when, kind, value, line in events if kind == "api_error"]
        reconcile_session(report, actual, key, project, path, events)
    check_errors(report, actual, api_errors)
    check_timeouts(report, actual)
    text = render(report, since, actual, sessions)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text)
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(text)


if __name__ == "__main__":
    main()
