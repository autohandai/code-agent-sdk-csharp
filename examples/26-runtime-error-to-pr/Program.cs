using Autohand.CodeAgentSdk;

static double CheckoutDiscount(Cart cart)
{
    try
    {
        return cart.Customer!.LoyaltyTier == "gold"
            ? cart.Subtotal * 0.15
            : cart.Subtotal * 0.05;
    }
    catch (Exception error)
    {
        throw new InvalidOperationException($"checkout discount failed: {error.Message}", error);
    }
}

static string CaptureRuntimeError()
{
    try
    {
        _ = CheckoutDiscount(new Cart(129, null));
    }
    catch (Exception error)
    {
        return error.ToString();
    }

    return string.Join('\n',
        "InvalidOperationException: checkout discount failed: Object reference not set to an instance of an object.",
        "    at Checkout.Discounts.CheckoutDiscount(Cart cart) in src/Checkout/Discounts.cs:line 42",
        "    at Checkout.Session.CreateCheckoutSession() in src/Checkout/Session.cs:line 88",
        "Request: POST /checkout",
        "Payload: {\"subtotal\":129,\"customer\":null}");
}

var targetRepo = Environment.GetEnvironmentVariable("AUTOHAND_TARGET_REPO") ?? ".";
var capturedError = CaptureRuntimeError();

var options = new AgentOptions
{
    WorkingDirectory = targetRepo,
    CliPath = Environment.GetEnvironmentVariable("AUTOHAND_CLI_PATH"),
    Model = Environment.GetEnvironmentVariable("AUTOHAND_MODEL"),
    Instructions = string.Join('\n',
        "You are a QA engineering agent that turns production error reports into small repair pull requests.",
        "Reproduce the failure when the repository makes that possible.",
        "Fix the root cause, add or update a focused regression test, run the relevant validation command, commit the fix, push a branch, and create a pull request.",
        "Keep the pull request description concise and include the error signature, the fix summary, and the validation result."),
};

var prompt = string.Join('\n',
    "A runtime error was captured by the application error boundary.",
    "Use this error report to repair the application automatically.",
    "",
    "Captured error:",
    "```text",
    capturedError,
    "```",
    "",
    "Expected user impact:",
    "A checkout session should still calculate a safe default discount when the customer object is missing.",
    "",
    "Please create a pull request with the fix.");

await using var agent = await Agent.CreateAsync(options);
var run = agent.Send(prompt);

await foreach (var item in run.StreamAsync())
{
    switch (item)
    {
        case MessageUpdateEvent message:
            Console.Write(message.Delta);
            break;
        case ToolStartEvent tool:
            Console.WriteLine($"\n[tool] {tool.ToolName}");
            break;
        case PermissionRequestEvent permission:
            Console.WriteLine($"\n[permission] {permission.Tool}: {permission.Description}");
            break;
        case ErrorEvent error:
            Console.Error.WriteLine($"\n[error] {error.Message}");
            break;
    }
}

var result = await run.WaitAsync();
Console.WriteLine($"\n\nRun {result.Id} {result.Status}.");

internal sealed record Cart(double Subtotal, Customer? Customer);

internal sealed record Customer(string LoyaltyTier);
