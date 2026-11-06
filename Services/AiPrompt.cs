namespace Toetsr.Services;

/// <summary>Kant-en-klare prompt om met ChatGPT/Claude vragen in het Dino-formaat te laten maken.
/// Houd deze tekst gelijk met de prompt in README.md.</summary>
public static class AiPrompt
{
    public const string Placeholder = "[VUL HIER HET ONDERWERP OF DE LEERSTOF IN]";

    public const string Text = """
        Maak een oefentoets over het volgende onderwerp of de volgende leerstof:

        [VUL HIER HET ONDERWERP OF DE LEERSTOF IN]

        Geef ALLEEN geldige JSON terug, zonder uitleg ervoor of erna, in precies dit formaat:

        {
          "title": "Titel van de toets",
          "description": "Korte omschrijving (optioneel)",
          "shuffleQuestions": false,
          "shuffleOptions": false,
          "questions": [
            {
              "type": "single",
              "title": "Onderwerp of hoofdstuk (optioneel)",
              "text": "Vraagtekst. Markdown mag, ook codeblokken met ```.",
              "points": 1,
              "explanation": "Uitleg waarom het goede antwoord goed is.",
              "options": [
                { "text": "Goed antwoord", "correct": true },
                { "text": "Fout antwoord" },
                { "text": "Fout antwoord" },
                { "text": "Fout antwoord" }
              ]
            },
            {
              "type": "multiple",
              "text": "Welke antwoorden zijn juist? (meerdere goed)",
              "points": 2,
              "explanation": "Uitleg.",
              "options": [
                { "text": "Goed", "correct": true },
                { "text": "Ook goed", "correct": true },
                { "text": "Fout" },
                { "text": "Fout" }
              ]
            },
            {
              "type": "dropdown",
              "text": "De hoofdstad van Frankrijk is {{1}} en die van Duitsland is {{2}}.",
              "points": 2,
              "explanation": "Uitleg.",
              "gaps": [
                { "options": [ { "text": "Parijs", "correct": true }, { "text": "Lyon" }, { "text": "Marseille" } ] },
                { "options": [ { "text": "Berlijn", "correct": true }, { "text": "München" }, { "text": "Hamburg" } ] }
              ]
            }
          ]
        }

        Regels:
        - "type" is "single" (precies één goed antwoord), "multiple" (één of meer goede antwoorden) of "dropdown" (tekst met gaten).
        - Bij "single" en "multiple": minimaal 2, liefst 4 opties in "options". Markeer goede antwoorden met "correct": true.
        - Bij "dropdown": zet {{1}}, {{2}}, ... in de tekst. "gaps" bevat per gat (in dezelfde volgorde) minimaal 2 keuzes met precies één "correct": true.
        - Schrijf bij elke vraag een korte, duidelijke "explanation".
        - Foute antwoorden moeten geloofwaardig zijn, niet overduidelijk fout.
        - Maak 10 vragen, met een mix van alle drie de types.
        - Schrijf in het Nederlands, tenzij de leerstof in een andere taal is.
        """;
}
