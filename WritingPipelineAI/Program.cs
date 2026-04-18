using LlmTornado;
using LlmTornado.Agents;
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

        while (true)
        {
            Console.Write("Enter a writing task (/exit to quit): ");
            string? task = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(task))
                continue;

            if (task.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            // TODO:
            // 1. Ask the writer for a first draft
            // 2. Ask the editor to review the draft
            // 3. Parse the editor response into STATUS, RATIONALE, and REVISION TASKS
            // 4. If STATUS is REVISE, ask the writer to revise the draft
            // 5. Repeat until STATUS is READY or max rounds reached
            // 6. Display the final approved draft or the most recent draft
        }
    }

    static TornadoAgent CreateWriterAgent(TornadoApi api)
    {
        var writer = new TornadoAgent(
        client: api,
        model: new ChatModel("google/gemma-3-4b"),
        instructions: """
		 You are a writing assistant... FINISH MY INSTRUCTIONS PROMPT
		 """
       );
        return writer;
    }

    static TornadoAgent CreateEditorAgent(TornadoApi api)
    {
        // TODO: return a configured Editor agent
        throw new NotImplementedException();
    }

    static EditorialReview ParseReview(string editorResponse)
    {
        // TODO: extract STATUS, RATIONALE, and REVISION TASKS
        throw new NotImplementedException();
    }
}
