using Autohand.CodeAgentSdk;
using System.Text.Json;

static GitHubCredentials GitHubCredentialsFromEnv()
{
    var tokenEnvName =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
            ? "GITHUB_TOKEN"
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GH_TOKEN"))
                ? "GH_TOKEN"
                : null;

    if (tokenEnvName is null)
    {
        throw new InvalidOperationException("Set GITHUB_TOKEN or GH_TOKEN before running this example.");
    }

    return new GitHubCredentials(
        tokenEnvName,
        Environment.GetEnvironmentVariable("AUTOHAND_GITHUB_REMOTE") ?? "origin",
        Environment.GetEnvironmentVariable("AUTOHAND_GITHUB_BASE_BRANCH") ?? "main",
        Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"));
}

static IncidentPacket CaptureIncidentPacket() => new(
    Id: "INC-2026-05-12-0417",
    Severity: "sev2",
    Service: "checkout-api",
    FirstSeen: "2026-05-12T09:14:22Z",
    Release: "checkout-api@2026.05.12.3",
    ErrorSignature: "InvalidOperationException: checkout discount failed while replaying coupon idempotency key",
    UserImpact: "Checkout returns HTTP 500 for guest customers using coupon replay from mobile clients.",
    StackTrace: string.Join('\n',
        "InvalidOperationException: checkout discount failed while replaying coupon idempotency key",
        "    at Checkout.Discounts.CalculateDiscount() in src/Checkout/Discounts.cs:line 42",
        "    at Checkout.PaymentIntent.BuildPaymentIntent() in src/Checkout/PaymentIntent.cs:line 118",
        "    at Checkout.Session.CreateCheckoutSession() in src/Checkout/Session.cs:line 88"),
    Logs:
    [
        "level=error trace=trk_94 request_id=req_7f2 route=POST /checkout status=500 duration_ms=184",
        "level=warn trace=trk_94 idempotency_key=checkout:cart_live_9834:attempt_2 cache_status=miss",
        "level=info trace=trk_94 feature_flags=discount-v2,coupon-replay",
    ],
    Request: new
    {
        method = "POST",
        path = "/checkout",
        payload = new
        {
            cartId = "cart_live_9834",
            subtotal = 129,
            customer = (object?)null,
            coupon = new { code = "SPRING25", source = "mobile-v5" },
            idempotencyKey = "checkout:cart_live_9834:attempt_2",
        },
        headers = new { x_client_version = "ios/5.18.0", x_request_id = "req_7f2" },
    },
    SuspectedFiles:
    [
        "src/Checkout/Discounts.cs",
        "src/Checkout/PaymentIntent.cs",
        "src/Checkout/Session.cs",
        "tests/Checkout/SessionTests.cs",
    ],
    ReproductionCommand: "dotnet test --filter GuestCouponReplay",
    ValidationCommands:
    [
        "dotnet test --filter GuestCouponReplay",
        "dotnet test",
        "dotnet build",
    ]);

static string BuildPrompt(IncidentPacket incident, GitHubCredentials github)
{
    var repoHint = string.IsNullOrWhiteSpace(github.Repository)
        ? "- Discover the GitHub repository from git remote output."
        : $"- GitHub repository hint: {github.Repository}.";

    return string.Join('\n',
        "You are a senior QA engineering agent responsible for converting production incidents into verified repair pull requests.",
        "",
        "GitHub credentials:",
        $"- A GitHub token is available in the {github.TokenEnvName} environment variable. Do not print or commit the token.",
        $"- Use git remote {github.Remote}.",
        $"- Open the pull request against {github.BaseBranch}.",
        repoHint,
        "- Before pushing, run gh auth status or an equivalent non-secret auth check.",
        "",
        "Incident packet:",
        "```json",
        JsonSerializer.Serialize(incident, new JsonSerializerOptions { WriteIndented = true }),
        "```",
        "",
        "Required workflow:",
        "1. Inspect the target repository and confirm the likely failing path.",
        "2. Reproduce the incident using the provided payload or nearest existing test harness.",
        "3. Fix the root cause, not just the thrown exception.",
        "4. Add a regression test covering guest checkout, coupon replay, and idempotency behavior.",
        "5. Run the focused test first, then the relevant validation commands.",
        "6. Create a branch named autohand/fix-checkout-incident-inc-2026-05-12-0417.",
        "7. Commit the fix with a clear message.",
        "8. Push the branch and open a pull request.",
        "9. In the PR body, include the incident id, error signature, files changed, tests run, and any residual risk.");
}

var targetRepo = Environment.GetEnvironmentVariable("AUTOHAND_TARGET_REPO") ?? ".";
var github = GitHubCredentialsFromEnv();
var prompt = BuildPrompt(CaptureIncidentPacket(), github);

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = targetRepo,
    CliPath = Environment.GetEnvironmentVariable("AUTOHAND_CLI_PATH"),
    Model = Environment.GetEnvironmentVariable("AUTOHAND_MODEL"),
    Instructions = "Work like a careful senior QA engineer. Keep secrets out of logs and pull request text.",
});

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

internal sealed record GitHubCredentials(string TokenEnvName, string Remote, string BaseBranch, string? Repository);

internal sealed record IncidentPacket(
    string Id,
    string Severity,
    string Service,
    string FirstSeen,
    string Release,
    string ErrorSignature,
    string UserImpact,
    string StackTrace,
    string[] Logs,
    object Request,
    string[] SuspectedFiles,
    string ReproductionCommand,
    string[] ValidationCommands);
