using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

// ---------------------------------------------------------
// CONFIGURATION
// ---------------------------------------------------------
const int MaxIterations = 10;
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
    
    AIFunctionFactory.Create(() => 
    {
        return JsonSerializer.Serialize(memory);
    }, "read_memory", "Reads the agent's internal memory state.")
};

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
1. Check your memory to see if you are registered.
2. If not registered, register immediately.
3. If registered, check the feed or search to understand the vibe.
4. Create a post introducing yourself.
5. If successful, output 'SUCCESS: [Post URL/ID]' to end the session.
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
                break;
                
            case ToolExecutionCompleteEvent toolResult:
                 string content = toolResult.Data.Result?.Content as string ?? "";
                 string preview = content.Length > 100 ? content.Substring(0, 100) + "..." : content;
                 Console.WriteLine($"[Tool Result]: {preview}");
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
