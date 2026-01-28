# Learning Path: Model Context Protocol (MCP) C# SDK

The Model Context Protocol (MCP) C# SDK empowers .NET developers to build applications that seamlessly integrate with Large Language Models (LLMs) by standardizing context provision.

## 1. Understand the Core Purpose
*   **What is MCP?** An open protocol for standardizing how applications provide context (data, tools) to LLMs.
*   **Why use the C# SDK?** To implement MCP clients and servers in .NET, enabling secure and structured interaction between your applications and LLMs.

## 2. Installation & Setup
*   **Prerequisites:** .NET SDK.
*   **NuGet Packages:**
    *   `ModelContextProtocol`: Main package for hosting and dependency injection (most common).
    *   `ModelContextProtocol.AspNetCore`: For building HTTP-based MCP servers.
    *   `ModelContextProtocol.Core`: For low-level client/server APIs with minimal dependencies.
*   **Installation (CLI):**
    ```bash
    dotnet add package ModelContextProtocol --prerelease
    # For server hosting
    dotnet add package Microsoft.Extensions.Hosting 
    ```
*   **Important Note:** The SDK is in preview; expect potential breaking changes.

## 3. Key Architectural Components
*   **`McpServer`:** The central component for creating an MCP server. It manages tool registration and handles incoming client requests.
*   **`McpClient`:** Used by applications to connect to an MCP server, discover available tools, and invoke them.
*   **Transports:** Mechanisms for communication.
    *   `StdioServerTransport`: For server communication over standard input/output.
    *   `StdioClientTransport`: For client communication over standard input/output.
    *   HTTP-based transports are available via `ModelContextProtocol.AspNetCore`.
*   **Tools:** Functions or capabilities exposed by an MCP server that LLMs can call.

## 4. Building a Simple MCP Server
*   **Goal:** Expose a basic function (e.g., an "Echo" tool) to an MCP client.
*   **Process:**
    1.  Create a .NET Host application.
    2.  Add `ModelContextProtocol` and `Microsoft.Extensions.Hosting` NuGet packages.
    3.  Configure the host to add `McpServer` services and use a transport (e.g., `WithStdioServerTransport()`).
    4.  Use `WithToolsFromAssembly()` to automatically discover tools.
    5.  Define your tool as a static method within a class, decorated with `[McpServerToolType]` and `[McpServerTool]`. Use `[Description]` for documentation.

*   **Example Code:**
    ```csharp
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using ModelContextProtocol.Server;
    using System.ComponentModel;

    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        // Ensure logs go to stderr so they don't interfere with StdIO transport
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();

    [McpServerToolType]
    public static class EchoTool
    {
        [McpServerTool, Description("Echoes the message back to the client.")]
        public static string Echo(string message) => $"hello {message}";
    }
    ```

## 5. Interacting with an MCP Client
*   **Goal:** Connect to an MCP server and invoke its tools.
*   **Process:**
    1.  Instantiate an `McpClient` using a client transport (e.g., `StdioClientTransport`).
    2.  Use `client.ListToolsAsync()` to discover available tools.
    3.  Use `client.CallToolAsync()` to invoke a specific tool with arguments.
*   **Example Code (Client-side snippet):**
    ```csharp
    using ModelContextProtocol.Client;
    using ModelContextProtocol.Protocol;
    // ... other usings

    var clientTransport = new StdioClientTransport(new StdioClientTransportOptions {
        Name = "MyClient",
        Command = "dotnet", // Command to start the server process
        Arguments = ["run", "--project", "Path/To/Your/ServerProject.csproj"]
    });
    // Create client
    var client = await McpClient.CreateAsync(clientTransport);

    // List tools
    foreach (var tool in await client.ListToolsAsync()) {
        Console.WriteLine($"{tool.Name} ({tool.Description})");
    }

    // Call the "echo" tool
    var result = await client.CallToolAsync(
        "echo",
        new Dictionary<string, object?>() { ["message"] = "Hello MCP!" },
        cancellationToken: CancellationToken.None
    );
    Console.WriteLine(result.Content.OfType<TextContentBlock>().First().Text);
    ```

## 6. Advanced Concepts (Explore Further)
*   **Dependency Injection:** Tools can have `McpServer` or other services injected as parameters.
*   **Prompts:** Expose predefined prompts using `[McpServerPromptType]` and `[McpServerPrompt]`.
*   **Fine-grained Control:** Manually configure `McpServerOptions` for custom handlers and server information.
*   **AI Function Integration:** `McpClientTool` inherits from `AIFunction`, allowing easy integration with LLM SDKs.
*   **Error Handling:** Understand `McpProtocolException` and error codes.
