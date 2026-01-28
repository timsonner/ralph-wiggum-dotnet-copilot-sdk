using System.ComponentModel;
using System.Diagnostics;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

// ---------------------------------------------------------
// CONFIGURATION
// ---------------------------------------------------------
const int MaxIterations = 10;
string SolutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
string TargetProjectDir = Path.Combine(SolutionRoot, "TargetApp");
string TestProjectDir = Path.Combine(SolutionRoot, "TargetApp.Tests");

Console.WriteLine($"[Ralph] Starting... Root: {SolutionRoot}");

// ---------------------------------------------------------
// TOOLS
// ---------------------------------------------------------
var tools = new List<AIFunction>
{
    AIFunctionFactory.Create(async () => 
    {
        Console.WriteLine("[Tool] Running tests...");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test {TestProjectDir}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
            return "Tests PASSED. \n" + output;
        else
            return "Tests FAILED. \n" + output + "\nErrors:\n" + error;
    }, "run_tests", "Runs the unit tests. Returns the output."),

    AIFunctionFactory.Create((string path) => 
    {
        var fullPath = Path.Combine(SolutionRoot, path);
        if (!File.Exists(fullPath)) return $"File not found: {fullPath}";
        return File.ReadAllText(fullPath);
    }, "read_file", "Reads a file from the solution. Provide relative path (e.g., 'TargetApp/Calculator.cs')."),

    AIFunctionFactory.Create((string path, string content) => 
    {
        var fullPath = Path.Combine(SolutionRoot, path);
        File.WriteAllText(fullPath, content);
        return $"File written: {fullPath}";
    }, "write_file", "Writes content to a file. Provide relative path."),
    
    AIFunctionFactory.Create(() => 
    {
        // Simple list of relevant files to help the agent discover
        return string.Join("\n", Directory.GetFiles(TargetProjectDir, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(TestProjectDir, "*.cs", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(SolutionRoot, f)));
    }, "list_files", "Lists all C# files in the target projects.")
};

// ---------------------------------------------------------
// CLIENT SETUP
// ---------------------------------------------------------
// Note: In a real scenario, ensure 'github-copilot-cli' is installed and authenticated.
await using var client = new CopilotClient();
await client.StartAsync();

bool taskComplete = false;
int iteration = 0;

// ---------------------------------------------------------
// THE RALPH WIGGUM LOOP
// ---------------------------------------------------------
while (!taskComplete && iteration < MaxIterations)
{
    iteration++;
    Console.WriteLine($"\n--- [Ralph] Iteration {iteration} ---");

    // 1. Check status (Run tests) to see if we are done
    // We run this *outside* the agent first to check termination condition, 
    // OR we let the agent run it. Let's let the agent run it to decide what to do.
    
    // We create a FRESH session each time to ensure it sees the latest file state 
    // and doesn't get confused by stale context.
    var config = new SessionConfig
    {
        Model = "claude-sonnet-4.5", // or "gpt-4"
        Tools = tools,
        SystemMessage = new SystemMessageConfig
        {
            Content = "You are an autonomous repair agent. " +
                        "Your goal is to make all tests pass. " +
                        "1. List files to locate the code. " +
                        "2. Run tests to see failures. " +
                        "3. Read the failing code. " +
                        "4. Fix the code using 'write_file'. " +
                        "5. Run tests again to verify. " +
                        "If tests pass, say 'SUCCESS' and stop."
        }
    };

    await using var session = await client.CreateSessionAsync(config);

    // Initial prompt for this turn
    await session.SendAsync(new MessageOptions 
    { 
        Prompt = $"Iteration {iteration}. Current status? (Please run tests first)" 
    });

    // Wait for this turn to complete
    bool turnSuccess = await ProcessSessionUntilIdleAsync(session);

    if (turnSuccess)
    {
        Console.WriteLine("[Ralph] Agent reported success! Verifying one last time...");
        // Double check
        // (In a real impl, we'd parse the tool output or check a flag)
        // For this demo, we'll assume if the agent says "SUCCESS" we break, 
        // or we could just run the test tool directly here.
    }
}

if (taskComplete)
    Console.WriteLine("[Ralph] I'm helping! (All tests passed)");
else
    Console.WriteLine("[Ralph] I tried my best, but ran out of iterations.");


// ---------------------------------------------------------
// EVENT HANDLER
// ---------------------------------------------------------
async Task<bool> ProcessSessionUntilIdleAsync(CopilotSession session)
{
    var tcs = new TaskCompletionSource<bool>();
    bool successSignaled = false;

    session.On(evt => 
    {
        switch(evt)
        {
            case AssistantMessageEvent msg:
                Console.WriteLine($"[AI]: {msg.Data.Content}");
                if (msg.Data.Content.Contains("SUCCESS"))
                {
                    successSignaled = true;
                    taskComplete = true; // Break the outer loop
                }
                break;
            
            case ToolExecutionStartEvent tool:
                Console.WriteLine($"[Tool]: Executing {tool.Data.ToolName}...");
                break;

            case ToolExecutionCompleteEvent toolResult:
                // Check if tests passed in the tool result
                if (toolResult.Data.Result?.Content.Contains("Tests PASSED") == true)
                {
                    Console.WriteLine("[Ralph] Tests passed during tool execution!");
                    // We could mark complete here, but let the agent confirm
                }
                break;

            case SessionIdleEvent: 
                tcs.TrySetResult(successSignaled); 
                break;

            case SessionErrorEvent err:
                Console.Error.WriteLine($"[Error]: {err.Data.Message}");
                tcs.TrySetResult(false);
                break;
        }
    });

    return await tcs.Task;
}