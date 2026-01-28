# The Ralph Wiggum Pattern in C# Copilot SDK

## 1. The Concept

The **Ralph Wiggum Loop** (named after the *Simpsons* character who says "I'm helping!") is an agentic pattern where an AI agent is placed in a continuous feedback loop.

**Core Logic:**
1.  **Action:** The agent attempts to perform a task (e.g., "Fix the build").
2.  **Feedback:** The environment (compiler, test runner) provides feedback (errors, logs).
3.  **Persistence:** The state is saved to the filesystem (the "memory").
4.  **Loop:** The process repeats, often with a fresh or truncated context, forcing the agent to rely on the current state of files rather than a long conversation history.

It is effectively: `while (task_not_complete) { agent.act(); }`

## 2. Why use it?

*   **Self-Healing:** Great for fixing compilation errors or failing tests. The agent iterates until green.
*   **Large Refactors:** Can chip away at a large migration task file by file.
*   **Statelessness:** By relying on the filesystem as the source of truth, you avoid context window limits.

## 3. C# Copilot SDK Implementation

In the C# SDK, this pattern is implemented by wrapping the `CopilotSession` in a standard C# control flow loop.

### Architecture

1.  **The Loop:** A `while` loop in your `Program.cs`.
2.  **The Check:** A condition to break the loop (e.g., a "Success" tool call or a passing test command).
3.  **The Session:** Often, a **new** session is created for each iteration (or every few) to prevent context bloat. The agent "sees" the work of its predecessor by reading the modified files.

### Blueprint Code

```csharp
using GitHub.Copilot.Sdk;
using Microsoft.Extensions.AI;

// 1. Setup
await using var client = new CopilotClient();
await client.StartAsync();

bool isTaskComplete = false;
int maxIterations = 10;
int currentIteration = 0;

var tools = new List<AIFunction>
{
    // Tool to run shell commands (tests, build)
    AIFunctionFactory.Create(async (string command) => 
    {
        var result = await RunShellCommandAsync(command);
        return result; 
    }, "run_command", "Executes a shell command."),

    // Tool for the agent to signal success
    AIFunctionFactory.Create(() => 
    { 
        isTaskComplete = true; 
        return "Marked as complete."; 
    }, "task_complete", "Call this when the goal is met (e.g. tests pass).")
};

// 2. The Ralph Loop
while (!isTaskComplete && currentIteration < maxIterations)
{
    currentIteration++;
    Console.WriteLine($"--- Iteration {currentIteration} ---");

    // Create a FRESH session (or resume if context is needed)
    // A fresh session forces the agent to look at the file system state.
    var config = new SessionConfig
    {
        Model = "gpt-4",
        Tools = tools,
        SystemMessage = "You are an iterative repair agent. " +
                        "Run the tests. If they fail, read the files and fix the errors. " +
                        "If they pass, call 'task_complete'."
    };

    await using var session = await client.CreateSessionAsync(config);

    // Initial prompt to kick off this iteration
    await session.SendAsync(new MessageOptions 
    { 
        Content = "Current status check. Run 'dotnet test' and fix any issues." 
    });

    // Wait for the turn to finish (handling tool calls along the way)
    await ProcessSessionUntilIdleAsync(session);
}

if (isTaskComplete)
{
    Console.WriteLine("I'm helping! (Task Complete)");
}
else
{
    Console.WriteLine("Max iterations reached.");
}

// Helper to handle the event loop for a single turn
async Task ProcessSessionUntilIdleAsync(CopilotSession session)
{
    var tcs = new TaskCompletionSource();
    
    session.On(evt => 
    {
        switch(evt)
        {
            case SessionIdleEvent: 
                tcs.TrySetResult(); 
                break;
            case AssistantMessageEvent msg:
                Console.WriteLine($"[Ralph]: {msg.Data.Content}");
                break;
            // Handle tool execution events automatically or manually here
        }
    });

    await tcs.Task;
}
```

## 4. Key Implementation Details

1.  **Tooling is Critical**: The agent *must* have tools to:
    *   `read_file`: See the current code.
    *   `write_file`: Make changes.
    *   `run_command`: verify its work (the feedback mechanism).
2.  **Prompt Engineering**: The prompt should encourage checking state first. "Don't assume, check the files."
3.  **Cost/Safety**: Always include a `maxIterations` guard to prevent infinite loops (and infinite billing).
