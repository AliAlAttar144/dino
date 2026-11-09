"""Trekt 30 willekeurige vragen uit de twee INFWAD-toetsen, in de verhouding van het echte
tentamen: 8 HTML/CSS/JS, 14 Vue en 8 Express. Schrijft infwad-4-proeftoets.json.

Gebruik (in deze map):  python nieuwe-mix.py
Daarna in Dino: Importeren > Map Toetsen/ > Map nu opnieuw inlezen.
"""
import json, os, random

here = os.path.dirname(os.path.abspath(__file__))

def load(name):
    with open(os.path.join(here, name), encoding="utf-8") as f:
        return json.load(f)["questions"]

fund = load("infwad-1-webfundamenten.json")
ve = load("infwad-2-vue-frontend.json")
vue = [q for q in ve if q["title"].startswith("Vue")]
exp = [q for q in ve if q["title"].startswith("Express")]

picked = random.sample(fund, 8) + random.sample(vue, 14) + random.sample(exp, 8)
random.shuffle(picked)

quiz = {
    "title": "INFWAD – Mix (30 willekeurige vragen)",
    "description": "30 willekeurige vragen uit beide toetsen, net als het echte tentamen: 8 HTML/CSS/JS, 14 Vue en 8 Express. "
                   "Nodig voor een 5,5: 20 van de 30 goed.\n\nWil je een nieuwe trekking? Draai `python nieuwe-mix.py` in de map Toetsen/ "
                   "en klik in Dino op Importeren > Map Toetsen/ > Map nu opnieuw inlezen.",
    "shuffleQuestions": True,
    "shuffleOptions": True,
    "questions": picked,
}
with open(os.path.join(here, "infwad-4-proeftoets.json"), "w", encoding="utf-8") as f:
    json.dump(quiz, f, ensure_ascii=False, indent=2)
print(f"Nieuwe mix gemaakt: {len(picked)} vragen.")
