using Autohand.CodeAgentSdk.Examples;

await ExampleSupport.RunAgentAsync(
    "03 Code Reviewer",
    "Review this repository as if preparing an SDK release. Focus on public API risks, docs gaps, and missing tests.");
