using System.Reflection;
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
    static async Task Main()
    {
        TornadoApi api = new TornadoApi(
            new Uri("http://127.0.0.1:1234"),
            string.Empty,
            LLmProviders.OpenAi);

        TornadoAgent writer = CreateWriterAgent(api);
        TornadoAgent editor = CreateEditorAgent(api);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Writing Pipeline AI - Writer + Editor");
        Console.ResetColor();

        while (true)
        {
            Console.Write("Enter a writing task (/exit to quit): ");
            string? task = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(task))
                continue;

            if (task.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            // 1. Ask the writer for a first draft
            Conversation writerConversation = await RunAgentAsync(writer, $"Task: {task}\n\nCreate the first draft.");
            string currentDraft = writerConversation.Messages.LastOrDefault()?.Content?.Trim() ?? "";

            int revisionRounds = 0;
            int draftRound = 1;

            StringBuilder transcript = new();
            transcript.AppendLine($"Task: {task}");
            transcript.AppendLine();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n--- DRAFT: ROUND {draftRound} ---");
                Console.ResetColor();
                Console.WriteLine(currentDraft);

                transcript.AppendLine($"--- DRAFT: ROUND {draftRound} ---");
                transcript.AppendLine(currentDraft);
                transcript.AppendLine();

                // 2. Ask the editor to review the draft
                Conversation editorConversation = await RunAgentAsync(editor,
                    $"Task: {task}\n\nDraft:\n{currentDraft}\n\nReview using the required format.");
                string editorResponse = editorConversation.Messages.LastOrDefault()?.Content?.Trim() ?? "";

                // 3. Parse the editor response into STATUS, RATIONALE, and REVISION TASKS
                EditorialReview review = ParseReview(editorResponse);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n--- EDITOR RESPONSE ---");
                Console.ResetColor();
                Console.WriteLine(editorResponse);

                transcript.AppendLine("--- EDITOR RESPONSE ---");
                transcript.AppendLine(editorResponse);
                transcript.AppendLine();

                // 5. Repeat until STATUS is READY or max rounds reached
                if (review.Status.Equals("READY", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\n--- FINAL APPROVED DRAFT ---");
                    Console.ResetColor();
                    Console.WriteLine(currentDraft);

                    transcript.AppendLine("--- FINAL APPROVED DRAFT ---");
                    transcript.AppendLine(currentDraft);
                    break;
                }

                if (revisionRounds >= 3)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n--- MAX REVISION ROUNDS REACHED ---");
                    Console.ResetColor();
                    Console.WriteLine(currentDraft);

                    transcript.AppendLine("--- MAX REVISION ROUNDS REACHED ---");
                    transcript.AppendLine(currentDraft);
                    break;
                }

                // 4. If STATUS is REVISE, ask the writer to revise the draft
                string revisionTasks = review.RevisionTasks.Count > 0
                    ? string.Join(Environment.NewLine, review.RevisionTasks.Select((t, i) => $"{i + 1}. {t}"))
                    : "1. Improve clarity for beginners.\n2. Add one short C# example.\n3. Strengthen flow and organization.";

                Conversation revisedConversation = await RunAgentAsync(writer,
                    $"Task: {task}\n\nCurrent draft:\n{currentDraft}\n\nRevise using these tasks:\n{revisionTasks}\n\nReturn only the revised draft.");

                currentDraft = revisedConversation.Messages.LastOrDefault()?.Content?.Trim() ?? currentDraft;
                revisionRounds++;
                draftRound++;
            }

            string outputPath = SaveOutput(transcript.ToString());
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"\nSaved output to: {outputPath}\n");
            Console.ResetColor();
        }
    }

    static TornadoAgent CreateWriterAgent(TornadoApi api)
    {
        var writer = new TornadoAgent(
        client: api,
        model: new ChatModel("google/gemma-3-4b"),
        instructions: """
         You are a writing assistant for beginner readers. Write a clear, organized draft based on the user's request.
         Use simple language, stay focused on the topic, and keep the writing concise. Output only the draft.
         """
       );
        return writer;
    }

    static TornadoAgent CreateEditorAgent(TornadoApi api)
    {
        return new TornadoAgent(
            client: api,
            model: new ChatModel("google/gemma-3-4b"),
            instructions: """
                You are an editor reviewing writing for beginner readers. Decide whether the draft is ready.
                Your response must follow this exact format:
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

        Match tasksSection = Regex.Match(editorResponse, @"REVISION TASKS\s*:\s*(?<tasks>[\s\S]*)$", RegexOptions.IgnoreCase);
        if (tasksSection.Success)
        {
            review.RevisionTasks = Regex.Matches(tasksSection.Groups["tasks"].Value, @"^\s*\d+[\.)]\s*(.+)$", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3)
                .ToList();
        }

        return review;
    }

    static string SaveOutput(string content)
    {
        string outputDir = Path.Combine(AppContext.BaseDirectory, "outputs");
        Directory.CreateDirectory(outputDir);

        string filePath = Path.Combine(outputDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    static async Task<Conversation> RunAgentAsync(TornadoAgent agent, string input)
    {
        MethodInfo? runAsyncMethod = FindAgentMethod(agent.GetType(), "RunAsync");
        if (runAsyncMethod is not null)
        {
            object? asyncResult = runAsyncMethod.Invoke(agent, BuildInvocationArguments(runAsyncMethod, input));
            if (asyncResult is Task<Conversation> typedTask)
            {
                return await typedTask;
            }

            if (asyncResult is Task genericTask)
            {
                await genericTask;
                object? resultValue = genericTask.GetType().GetProperty("Result")?.GetValue(genericTask);
                if (resultValue is Conversation conversationFromTask)
                {
                    return conversationFromTask;
                }
            }
        }

        MethodInfo? runMethod = FindAgentMethod(agent.GetType(), "Run");
        if (runMethod is not null)
        {
            object? runResult = runMethod.Invoke(agent, BuildInvocationArguments(runMethod, input));
            if (runResult is Conversation directConversation)
            {
                return directConversation;
            }

            if (runResult is Task<Conversation> taskConversation)
            {
                return await taskConversation;
            }

            if (runResult is Task genericRunTask)
            {
                await genericRunTask;
                object? resultValue = genericRunTask.GetType().GetProperty("Result")?.GetValue(genericRunTask);
                if (resultValue is Conversation conversationFromRunTask)
                {
                    return conversationFromRunTask;
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Could not find a compatible TornadoAgent Run/RunAsync method.");
        Console.ResetColor();
        return new Conversation();
    }

    static MethodInfo? FindAgentMethod(Type agentType, string methodName)
    {
        return agentType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    return false;

                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length >= 1 && parameters[0].ParameterType == typeof(string);
            });
    }

    static object?[] BuildInvocationArguments(MethodInfo method, string input)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object?[] args = new object?[parameters.Length];
        args[0] = input;

        for (int i = 1; i < parameters.Length; i++)
        {
            if (parameters[i].HasDefaultValue)
            {
                args[i] = parameters[i].DefaultValue;
            }
            else
            {
                Type pType = parameters[i].ParameterType;
                args[i] = pType.IsValueType ? Activator.CreateInstance(pType) : null;
            }
        }

        return args;
    }
}
