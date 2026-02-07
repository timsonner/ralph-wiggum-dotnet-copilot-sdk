using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// ---------------------------------------------------------
// CONFIGURATION
// ---------------------------------------------------------
const int MaxIterations = 30;
string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
string MemoryPath = Path.Combine(ProjectRoot, "memory.json");
string SkillsPath = Path.Combine(ProjectRoot, "skills.md");
string GoalPath = Path.Combine(ProjectRoot, "goal.md");

Console.WriteLine($"[Ralph] Starting... Root: {ProjectRoot}");

// ---------------------------------------------------------
// STATE MANAGEMENT & HTTP CLIENT
// ---------------------------------------------------------
AgentMemory memory = new AgentMemory();
if (File.Exists(MemoryPath))
{
    try
    {
        var json = File.ReadAllText(MemoryPath);
        memory = JsonSerializer.Deserialize<AgentMemory>(json) ?? new AgentMemory();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Ralph] Warning: Could not read memory file. Starting fresh. Error: {ex.Message}");
        memory = new AgentMemory();
    }
}

var httpClient = new HttpClient { BaseAddress = new Uri("https://www.moltbook.com/api/v1/") };

void SaveMemory()
{
    var json = JsonSerializer.Serialize(memory, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(MemoryPath, json);
}

void EnsureAuth()
{
    if (!string.IsNullOrEmpty(memory.ApiKey))
    {
        httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memory.ApiKey);
    }
}

// ---------------------------------------------------------
// TOOLS
// ---------------------------------------------------------
var tools = new List<AIFunction>
{
    AIFunctionFactory.Create((string key) => 
    {
        memory.ApiKey = key;
        SaveMemory();
        EnsureAuth();
        return "API Key saved manually.";
    }, "save_api_key", "Manually save the API key if registration auto-save fails."),

    AIFunctionFactory.Create(async (string name, string description) => 
    {
        Console.WriteLine($"[Tool] Registering agent: {name}");
        try 
        {
            var response = await httpClient.PostAsJsonAsync("agents/register", new { name, description });
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return $"Error: {response.StatusCode} - {content}";
            
            // Try to extract API key flexibly
            using var doc = JsonDocument.Parse(content);
            string? newKey = null;

            // 1. Check specific 'agent' object (Moltbook Standard)
            if (doc.RootElement.TryGetProperty("agent", out var agentObj))
            {
                if (agentObj.TryGetProperty("api_key", out var ak)) newKey = ak.GetString();
                if (agentObj.TryGetProperty("claim_url", out var cu)) memory.ClaimUrl = cu.GetString();
            }

            // 2. Fallback checks
            if (string.IsNullOrEmpty(newKey) && doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("token", out var t)) newKey = t.GetString();
                else if (doc.RootElement.TryGetProperty("api_key", out var k)) newKey = k.GetString();
                else if (doc.RootElement.TryGetProperty("key", out var key)) newKey = key.GetString();
            }

            if (!string.IsNullOrEmpty(newKey))
            {
                memory.ApiKey = newKey;
                memory.AgentName = name;
                SaveMemory();
                EnsureAuth();
                
                string msg = "Registration successful. ApiKey saved.";
                if (!string.IsNullOrEmpty(memory.ClaimUrl))
                    msg += $"\nIMPORTANT: You must visit this URL to claim the agent: {memory.ClaimUrl}";
                
                return msg;
            }

            return $"Registration succeeded but could not auto-parse token from: {content}. Please manually save it if possible.";
        }
        catch (Exception ex)
        {
            return $"Exception during registration: {ex.Message}";
        }
    }, "register_agent", "Register a new agent. Returns API key info."),

    AIFunctionFactory.Create(async (string sort) => 
    {
        EnsureAuth();
        Console.WriteLine("[Tool] Getting feed...");
        try {
            var response = await httpClient.GetAsync($"posts?sort={sort}");
            return await response.Content.ReadAsStringAsync();
        } catch (Exception ex) { return ex.Message; }
    }, "get_feed", "Gets the latest posts. Sort can be 'recent' or 'popular'."),

    AIFunctionFactory.Create(async (string title, string content, string submolt) => 
    {
        EnsureAuth();
        Console.WriteLine($"[Tool] Creating post: {title}");
         try {
            var response = await httpClient.PostAsJsonAsync("posts", new { title, content, submolt });
            var result = await response.Content.ReadAsStringAsync();
            memory.LastAction = DateTimeOffset.Now;
            SaveMemory();
            return result;
        } catch (Exception ex) { return ex.Message; }
    }, "create_post", "Creates a new post."),

    AIFunctionFactory.Create(async (string query) => 
    {
        EnsureAuth();
        Console.WriteLine($"[Tool] Searching: {query}");
         try {
            var response = await httpClient.GetAsync($"search?q={Uri.EscapeDataString(query)}");
            return await response.Content.ReadAsStringAsync();
        } catch (Exception ex) { return ex.Message; }
    }, "search", "Search for posts."),
    
    AIFunctionFactory.Create(async (string postId, string content, string? parentId) => 
    {
        EnsureAuth();
        Console.WriteLine($"[Tool] Adding comment to post {postId}");
        try {
            object payload = string.IsNullOrEmpty(parentId) 
                ? new { content = content }
                : (object)new { content = content, parent_id = parentId };
            var response = await httpClient.PostAsJsonAsync($"posts/{postId}/comments", payload);
            var result = await response.Content.ReadAsStringAsync();
            memory.LastAction = DateTimeOffset.Now;
            SaveMemory();
            return result;
        } catch (Exception ex) { return ex.Message; }
    }, "comment", "Add a comment to a post. Requires postId and content. Optional parentId for replies."),
    
    AIFunctionFactory.Create(() => 
    {
        return JsonSerializer.Serialize(memory);
    }, "read_memory", "Reads the agent's internal memory state.")
};

var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "KaliMCP",
    Command = "/bin/bash",
    Arguments = ["-c", "CONTAINER_ID=$(docker ps -q --filter ancestor=kali-mcp 2>/dev/null | head -1); if [ -n \"$CONTAINER_ID\" ]; then exec docker exec -i \"$CONTAINER_ID\" dotnet KaliMCP.dll; else exec docker run --rm -i --privileged --network host -v kali_mcp_data:/var/lib/docker kali-mcp; fi"]
}));

var mcpTools = await mcpClient.ListToolsAsync();
Console.WriteLine($"[Ralph] Found {mcpTools.Count} MCP tools from Kali server.");
foreach (var tool in mcpTools)
{
    Console.WriteLine($"[Ralph] Processing MCP tool: {tool.Name}");
    if (tool.Name == "kali-exec")
    {
        tools.Add(AIFunctionFactory.Create(async (string command, string? image, string? containerName) =>
        {
            try
            {
                var result = await mcpClient.CallToolAsync("kali-exec", new Dictionary<string, object?> { ["command"] = command, ["image"] = image, ["containerName"] = containerName });
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No output";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }, "kali-exec", "Execute a shell command in the persistent Kali Linux container."));
        Console.WriteLine($"[Ralph] Added tool: kali-exec");
    }
    else if (tool.Name == "kali-container-status")
    {
        tools.Add(AIFunctionFactory.Create(async (string? containerName) =>
        {
            try
            {
                var result = await mcpClient.CallToolAsync("kali-container-status", new Dictionary<string, object?> { ["containerName"] = containerName });
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No output";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }, "kali-container-status", "Check the status of the persistent Kali Linux container."));
        Console.WriteLine($"[Ralph] Added tool: kali-container-status");
    }
    else if (tool.Name == "kali-container-restart")
    {
        tools.Add(AIFunctionFactory.Create(async (string? image, string? containerName) =>
        {
            try
            {
                var result = await mcpClient.CallToolAsync("kali-container-restart", new Dictionary<string, object?> { ["image"] = image, ["containerName"] = containerName });
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No output";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }, "kali-container-restart", "Restart the persistent Kali Linux container."));
        Console.WriteLine($"[Ralph] Added tool: kali-container-restart");
    }
    else if (tool.Name == "kali-container-stop")
    {
        tools.Add(AIFunctionFactory.Create(async (string? containerName, bool removeContainer) =>
        {
            try
            {
                var result = await mcpClient.CallToolAsync("kali-container-stop", new Dictionary<string, object?> { ["containerName"] = containerName, ["removeContainer"] = removeContainer });
                return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No output";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }, "kali-container-stop", "Stop the persistent Kali Linux container."));
        Console.WriteLine($"[Ralph] Added tool: kali-container-stop");
    }
}

// ---------------------------------------------------------
// CLIENT SETUP
// ---------------------------------------------------------
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

    EnsureAuth();
    
    string skills = File.Exists(SkillsPath) ? File.ReadAllText(SkillsPath) : "No skills defined.";
    string goal = File.Exists(GoalPath) ? File.ReadAllText(GoalPath) : "No goal defined.";

    var config = new SessionConfig
    {
        Model = "claude-sonnet-4.5", 
        Tools = tools,
        SystemMessage = new SystemMessageConfig
        {
            Content = $@"
You are an autonomous agent using the Ralph pattern.

CONTEXT:
{skills}

GOAL:
{goal}

CURRENT MEMORY:
{JsonSerializer.Serialize(memory)}

Instructions:
1. Review the goal content carefully.
2. Execute the steps required to achieve the goal.
3. Use the available tools effectively.
4. When the goal is fully achieved according to the usage instructions, Output 'SUCCESS' with a summary.
"
        }
    };

    await using var session = await client.CreateSessionAsync(config);

    await session.SendAsync(new MessageOptions 
    { 
        Prompt = $"Iteration {iteration}. What is your next move?" 
    });

    bool turnSuccess = await ProcessSessionUntilIdleAsync(session);

    if (turnSuccess)
    {
        break;
    }
}

if (taskComplete)
    Console.WriteLine("[Ralph] Mission Accomplished.");
else
    Console.WriteLine("[Ralph] Max iterations reached.");


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
                    taskComplete = true; 
                }
                break;
            
            case ToolExecutionStartEvent tool:
                Console.WriteLine($"[Tool]: Executing {tool.Data.ToolName}...");
                if (tool.Data.Arguments != null)
                {
                    Console.WriteLine($"[Tool Args]: {JsonSerializer.Serialize(tool.Data.Arguments)}");
                }
                break;
                
            case ToolExecutionCompleteEvent toolResult:
                 string content = toolResult.Data.Result?.Content as string ?? "";
                 Console.WriteLine($"[Tool Result]: {content}");
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

// ---------------------------------------------------------
// TYPE DEFINITIONS
// ---------------------------------------------------------
class AgentMemory
{
    public string? ApiKey { get; set; }
    public string? AgentName { get; set; }
    public string? ClaimUrl { get; set; }
    public DateTimeOffset? LastAction { get; set; }
}
