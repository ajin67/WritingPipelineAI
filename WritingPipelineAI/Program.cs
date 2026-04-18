using System.Text;
using System.Text.RegularExpressions;
using LlmTornado;
using LlmTornado.Agents;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;

public class EditorialReview
{
    public string Status { get; set; } = "";
    public string Rationale { get; set; } = "";
    public List<string> RevisionTasks { get; set; } = new();
}

class Program
{
    private const int MaxRevisionRounds = 3;

    static async Task Main()
    {
        TornadoApi api = new TornadoApi(
            new Uri("http://127.0.0.1:1234"),
            string.Empty,
            LLmProviders.OpenAi);

        TornadoAgent writer = CreateWriterAgent(api);
        TornadoAgent editor = CreateEditorAgent(api);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Writing Pipeline AI (Writer + Editor) ===");
        Console.ResetColor();

        while (true)
        {
            Console.Write("Enter a writing task (/exit to quit): ");
            string? task = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(task))
                continue;

            if (task.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            string transcript = await RunPipeline(task, writer, editor);
            string savedPath = SaveOutput(transcript);

            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"\nSaved output to: {savedPath}\n");
            Console.ResetColor();
        }
    }

    static TornadoAgent CreateWriterAgent(TornadoApi api)
    {
        var writer = new TornadoAgent(
            client: api,
            model: new ChatModel("google/gemma-3-4b"),
            name: "Writer",
            instructions: """
             You are a writing assistant for beginner readers.
             Write a clear, organized draft based on the user's request.
             Use simple language, stay focused on the topic, and keep the writing concise.
             Output only the draft.
             """
       );
        return writer;
    }

    static TornadoAgent CreateEditorAgent(TornadoApi api)
    {
        return new TornadoAgent(
            client: api,
            model: new ChatModel("google/gemma-3-4b"),
            name: "Editor",
            instructions: """
                You are an editor reviewing writing for beginner readers.
                Decide whether the draft is ready. Your response must follow this exact format:
                STATUS: READY or REVISE
                RATIONALE: Write 1 short paragraph explaining your decision.
                REVISION TASKS: If the status is REVISE, provide exactly 3 specific revision tasks as a numbered list.
                If the status is READY, do not include revision tasks.
                """
        );
    }

    static EditorialReview ParseReview(string editorResponse)
    {
        EditorialReview review = new();

        Match statusMatch = Regex.Match(editorResponse, @"STATUS\s*:\s*(READY|REVISE)", RegexOptions.IgnoreCase);
        review.Status = statusMatch.Success ? statusMatch.Groups[1].Value.ToUpperInvariant() : "REVISE";

        Match rationaleMatch = Regex.Match(
            editorResponse,
            @"RATIONALE\s*:\s*(?<rationale>[\s\S]*?)(?:\n\s*REVISION TASKS\s*:|$)",
            RegexOptions.IgnoreCase);
        if (rationaleMatch.Success)
        {
            review.Rationale = rationaleMatch.Groups["rationale"].Value.Trim();
        }

        if (review.Status == "REVISE")
        {
            Match tasksSection = Regex.Match(editorResponse, @"REVISION TASKS\s*:\s*(?<tasks>[\s\S]*)$", RegexOptions.IgnoreCase);
            if (tasksSection.Success)
            {
                review.RevisionTasks = Regex.Matches(tasksSection.Groups["tasks"].Value, @"^\s*\d+[\.)]\s*(.+)$", RegexOptions.Multiline)
                    .Select(m => m.Groups[1].Value.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Take(3)
                    .ToList();
            }
        }

        return review;
    }

    static async Task<string> RunPipeline(string task, TornadoAgent writer, TornadoAgent editor)
    {
        StringBuilder transcript = new();
        int revisionRound = 0;
        int draftRound = 1;

        Conversation firstDraftConversation = await writer.RunAsync($"Task: {task}\n\nCreate the first draft.");
        string currentDraft = firstDraftConversation.Messages.LastOrDefault()?.Content?.Trim() ?? "";

        while (true)
        {
            PrintHeading($"--- DRAFT: ROUND {draftRound} ---", ConsoleColor.Green);
            Console.WriteLine(currentDraft);

            transcript.AppendLine($"--- DRAFT: ROUND {draftRound} ---");
            transcript.AppendLine(currentDraft);
            transcript.AppendLine();

            Conversation editorConversation = await editor.RunAsync(
                $"Task: {task}\n\nDraft:\n{currentDraft}\n\nReview now using the exact required format.");
            string editorResponse = editorConversation.Messages.LastOrDefault()?.Content?.Trim() ?? "";

            EditorialReview review = ParseReview(editorResponse);

            PrintHeading("--- EDITOR RESPONSE ---", ConsoleColor.Yellow);
            Console.WriteLine(editorResponse);
            Console.WriteLine();

            transcript.AppendLine("--- EDITOR RESPONSE ---");
            transcript.AppendLine(editorResponse);
            transcript.AppendLine();

            if (review.Status == "READY")
            {
                PrintHeading("--- FINAL APPROVED DRAFT ---", ConsoleColor.Cyan);
                Console.WriteLine(currentDraft);

                transcript.AppendLine("--- FINAL APPROVED DRAFT ---");
                transcript.AppendLine(currentDraft);
                break;
            }

            if (revisionRound >= MaxRevisionRounds)
            {
                PrintHeading("--- MAX REVISION ROUNDS REACHED ---", ConsoleColor.Red);
                Console.WriteLine(currentDraft);

                transcript.AppendLine("--- MAX REVISION ROUNDS REACHED ---");
                transcript.AppendLine(currentDraft);
                break;
            }

            revisionRound++;
            draftRound++;

            string revisionTasks = review.RevisionTasks.Count == 0
                ? "1. Improve clarity for beginners.\n2. Add one short concrete example.\n3. Tighten wording and flow."
                : string.Join(Environment.NewLine, review.RevisionTasks.Select((t, i) => $"{i + 1}. {t}"));

            Conversation revisedConversation = await writer.RunAsync(
                $"Task: {task}\n\nCurrent draft:\n{currentDraft}\n\nRevision tasks:\n{revisionTasks}\n\nReturn only the revised draft.");
            currentDraft = revisedConversation.Messages.LastOrDefault()?.Content?.Trim() ?? currentDraft;
        }

        return transcript.ToString();
    }

    static string SaveOutput(string content)
    {
        string outputDir = Path.Combine(AppContext.BaseDirectory, "outputs");
        Directory.CreateDirectory(outputDir);

        string filePath = Path.Combine(outputDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    static void PrintHeading(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine();
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
