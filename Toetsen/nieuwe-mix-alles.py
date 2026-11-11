"""Trekt 30 willekeurige vragen uit ALLE INFWAD-toetsen (ook de sticker-toetsen), in de verhouding
van het echte tentamen: 8 HTML/CSS/JavaScript/TypeScript (waarvan minstens 2 TypeScript), 14 Vue en 8 Express.
Vragen die je met X hebt uitgesloten worden overgeslagen. Schrijft infwad-8-mix-alles.json.

Gebruik (in deze map):  python nieuwe-mix-alles.py
Dino leest het bestand vanzelf opnieuw in (of: Importeren > Map nu opnieuw inlezen).
"""
import json, os, random, re

here = os.path.dirname(os.path.abspath(__file__))

def load(name):
    with open(os.path.join(here, name), encoding="utf-8") as f:
        return [q for q in json.load(f)["questions"] if not q.get("excluded")]

fund = load("infwad-1-webfundamenten.json") + load("infwad-5-oefentoets-fundamentals.json")
ve = load("infwad-2-vue-frontend.json")
vue = [q for q in ve if q["title"].startswith("Vue")] + load("infwad-6-stickers-vue.json")
exp = [q for q in ve if q["title"].startswith("Express")] + load("infwad-7-stickers-express.json")

def key(q):
    return re.sub(r"\W+", " ", q["text"].lower()).strip()

def sample(pool, n, taken):
    """n willekeurige vragen, zonder (bijna) dezelfde vraag twee keer."""
    pool = pool[:]
    random.shuffle(pool)
    out = []
    for q in pool:
        if key(q) not in taken:
            taken.add(key(q))
            out.append(q)
            if len(out) == n:
                break
    return out

taken = set()
ts = [q for q in fund if q.get("title", "").startswith("TypeScript")]
picked_fund = sample(ts, 2, taken)
picked_fund += sample([q for q in fund if q not in picked_fund], 8 - len(picked_fund), taken)
picked = picked_fund + sample(vue, 14, taken) + sample(exp, 8, taken)
random.shuffle(picked)

quiz = {
    "title": "INFWAD – Mix alles (30 vragen)",
    "description": "30 willekeurige vragen uit alle toetsen, inclusief de Vue- en Express-stickers, net als het echte tentamen: "
                   "8 HTML/CSS/JavaScript/TypeScript, 14 Vue en 8 Express. Nodig voor een 5,5: 20 van de 30 goed.\n\n"
                   "Wil je een nieuwe trekking? Draai `python nieuwe-mix-alles.py` in de map Toetsen/.",
    "shuffleQuestions": True,
    "shuffleOptions": True,
    "questions": picked,
}
with open(os.path.join(here, "infwad-8-mix-alles.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(quiz, f, ensure_ascii=False, indent=2)
    f.write("\n")
print(f"Nieuwe mix gemaakt: {len(picked)} vragen.")
