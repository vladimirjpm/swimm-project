#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Throwaway-скрипт для Шага 1 переноса dolphin_masters_data.json (см.
docs/tasks/hubgroups-dolphin-import.md).

Выписывает уникальные варианты пловцов из JSON и предварительно кластеризует их
(нормализуя birth_year и подхватывая англ. имена), чтобы Влад руками вычитал:
кто с кем один человек и кто уже есть в isr.org.il.

Запуск (из корня репозитория):
    python docs/tasks/dolphin-canon/build_canon_draft.py

Выход: docs/tasks/dolphin-canon/canon-draft.csv  (UTF-8 BOM, открывается в Excel)

Как заполнять CSV:
  - canon_group  — уже проставлена авто-догадка (номер кластера). ПРАВЬ её:
                   если два кластера — один человек, поставь им одинаковый номер;
                   если внутри кластера склеились разные люди — раздели номера.
  - existing_swimmer_id — если это пловец, уже существующий в isr.org.il,
                   впиши его Swimmers.Id (тогда local-двойник НЕ создаётся).
                   Иначе оставь пусто → будет создан Swimmer(Origin='local').
  - canon_name / canon_birth_year — итоговые имя/год для НОВОГО local-пловца
                   (для кластера бери самый полный вариант). Для existing — можно пусто.
"""
import json
import csv
import os
import re
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SRC = os.path.join(REPO, "client", "public", "data", "json", "dolphin_masters_data.json")
OUT = os.path.join(HERE, "canon-draft.csv")


def norm_year(v):
    """1978 / '1978' / '' / None -> '1978' или ''."""
    if v is None:
        return ""
    s = str(v).strip()
    m = re.search(r"\d{4}", s)
    return m.group(0) if m else ""


def norm_name(s):
    return (s or "").strip()


def main():
    with open(SRC, encoding="utf-8") as f:
        data = json.load(f)

    # ── собираем уникальные варианты по сырому ключу ──────────────────────────
    variants = {}  # raw_key -> aggregate dict
    for r in data:
        fn = norm_name(r.get("first_name"))
        ln = norm_name(r.get("last_name"))
        by = norm_year(r.get("birth_year"))
        fn_en = norm_name(r.get("first_name_en"))
        ln_en = norm_name(r.get("last_name_en"))
        gender = norm_name(r.get("event_style_gender"))
        is_train = bool(r.get("training"))
        raw_key = (fn, ln, by, fn_en, ln_en)

        v = variants.setdefault(raw_key, {
            "first_name": fn, "last_name": ln, "birth_year_raw": by,
            "first_name_en": fn_en, "last_name_en": ln_en,
            "gender": gender, "count": 0, "n_train": 0, "n_comp": 0,
            "events": set(),
        })
        v["count"] += 1
        v["n_train"] += 1 if is_train else 0
        v["n_comp"] += 0 if is_train else 1
        v["events"].add(norm_name(r.get("event")))
        if not v["gender"]:
            v["gender"] = gender

    # ── берём только тех, у кого есть тренировки (соревнования уже в БД) ───────
    variants = {k: v for k, v in variants.items() if v["n_train"] > 0}

    # ── авто-кластеризация union-find: один человек, если совпал год И есть ────
    # общий токен имени на любом языке (HE или EN). Точные слияния (מקס=מקסים)
    # авто не ловятся — их Влад доводит руками.
    vlist = list(variants.values())

    def tokens(v):
        raw = " ".join([v["first_name"], v["last_name"],
                        v["first_name_en"], v["last_name_en"]]).lower()
        return {t for t in re.split(r"\s+", raw) if t}

    parent = list(range(len(vlist)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        parent[find(a)] = find(b)

    for i in range(len(vlist)):
        for j in range(i + 1, len(vlist)):
            yi, yj = vlist[i]["birth_year_raw"], vlist[j]["birth_year_raw"]
            if yi and yj and yi == yj and (tokens(vlist[i]) & tokens(vlist[j])):
                union(i, j)

    # нумерация кластеров: по убыванию суммарного count
    groups = defaultdict(list)
    for i, v in enumerate(vlist):
        groups[find(i)].append(v)
    ordered = sorted(groups.values(), key=lambda vs: -sum(x["count"] for x in vs))
    group_of = {}
    for i, vs in enumerate(ordered, start=1):
        for v in vs:
            group_of[id(v)] = i

    # ── запись CSV ────────────────────────────────────────────────────────────
    rows = []
    for v in variants.values():
        rows.append({
            "canon_group": group_of[id(v)],
            "existing_swimmer_id": "",
            "canon_name": "",
            "canon_birth_year": "",
            "first_name": v["first_name"],
            "last_name": v["last_name"],
            "birth_year_raw": v["birth_year_raw"],
            "first_name_en": v["first_name_en"],
            "last_name_en": v["last_name_en"],
            "gender": v["gender"],
            "records": v["count"],
            "n_comp": v["n_comp"],
            "n_train": v["n_train"],
            "sample_events": "; ".join(sorted(v["events"])[:4]),
        })
    # сортируем по кластеру, потом по убыванию записей — чтобы дубли стояли рядом
    rows.sort(key=lambda r: (r["canon_group"], -r["records"]))

    cols = ["canon_group", "existing_swimmer_id", "canon_name", "canon_birth_year",
            "first_name", "last_name", "birth_year_raw", "first_name_en",
            "last_name_en", "gender", "records", "n_comp", "n_train", "sample_events"]

    with open(OUT, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        w.writerows(rows)

    n_clusters = len({r["canon_group"] for r in rows})
    print(f"variants: {len(rows)}  auto-clusters: {n_clusters}")
    print(f"records total: {sum(r['records'] for r in rows)} "
          f"(comp {sum(r['n_comp'] for r in rows)}, train {sum(r['n_train'] for r in rows)})")
    print(f"written: {OUT}")


if __name__ == "__main__":
    main()
