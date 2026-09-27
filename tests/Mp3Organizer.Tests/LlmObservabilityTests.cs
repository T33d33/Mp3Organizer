using System.Net;
using System.Net.Http;
using Mp3Organizer;
namespace Mp3Organizer.Tests;
public static partial class TestRunner
{
    static void LlmRequestTelemetry()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);var lines=new List<string>();
        using(var repo=new ProgressRepository(workspace))
        {
            var observer=new LlmObservability(lines.Add,repo);
            var handler=new MockHttp(_=>{Equal(handlerCount(),observer.Statistics.Requests);return Task.FromResult(Response("{}",HttpStatusCode.ServiceUnavailable));});
            long handlerCount()=>lines.Count(x=>x.StartsWith("LLM: requesting decision"));
            using var http=new HttpClient(handler);
            var context=new CodexContext("test.mp3",f.Source,TagMetadata.From(Row()),TagMetadata.From(Row()),100,[],new{},[],"test");
            var client=new CodexClient(http,"secret-key","test-model",(_,_)=>Task.CompletedTask,observer);
            Throws<CodexServiceException>(()=>client.DecideAsync(context).GetAwaiter().GetResult());Equal(3,handler.Calls);Equal(3L,observer.Statistics.Requests);Equal(3L,repo.LlmTotals().Requests);
            var missing=new CodexClient(http,"","test-model",observer:observer);Throws<CodexServiceException>(()=>missing.DecideAsync(context).GetAwaiter().GetResult());Equal(3L,observer.Statistics.Requests);Equal(3,handler.Calls);
            True(lines.All(x=>!x.Contains("secret-key")));True(lines.Last().Contains("attempt 3"));
        }
        using(var reopened=new ProgressRepository(workspace)){Equal(3L,reopened.LlmTotals().Requests);reopened.Clear();Equal(3L,reopened.LlmTotals().Requests);}
    }
    static void LlmOutcomeTelemetryDoesNotChangeDecisions()
    {
        var f=Fixture();using var repo=new ProgressRepository(ProgressWorkspace(f));var lines=new List<string>();var observer=new LlmObservability(lines.Add,repo);var state=new CodexRunState();var client=new FakeCodex();
        var good=CompleteYearRow() with{Year=2000};var actual=new CodexMetadataResolver(new FixedResolver(good),client,state,observer:observer).ResolveAsync(good,[good]).GetAwaiter().GetResult();Equal(JsonFormat.Serialize(good),JsonFormat.Serialize(actual));Equal(0,client.Calls);
        var row=AmbiguousRow();var expected=new CodexMetadataResolver(new FixedResolver(row),client,state).ResolveAsync(row,[row]).GetAwaiter().GetResult();
        actual=new CodexMetadataResolver(new FixedResolver(row),client,state,observer:observer).ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal(JsonFormat.Serialize(expected),JsonFormat.Serialize(actual));
        client.Decision=client.Decision with{HumanReviewRequired=true};actual=new CodexMetadataResolver(new FixedResolver(row),client,state,observer:observer).ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal("Review",actual.Identification!.Status);
        client.Failure=new(CodexFailureKind.Quota,"Usage limit");Throws<CodexPendingException>(()=>new CodexMetadataResolver(new FixedResolver(row),client,state,observer:observer).ResolveAsync(row,[row]).GetAwaiter().GetResult());
        state.Unavailable=client.Failure;observer.DisableForRun();var calls=client.Calls;actual=new CodexMetadataResolver(new FixedResolver(row),client,state,observer:observer).ResolveAsync(row,[row]).GetAwaiter().GetResult();Equal(calls,client.Calls);Equal("Review",actual.Identification!.Status);
        Equal(new LlmStatistics(0,1,1,1,2),observer.Statistics);Equal(observer.Statistics,repo.LlmTotals());
        True(lines.Any(x=>x.Contains("LLM: not required")));True(lines.Any(x=>x.Contains("LLM: resolved candidate candidate-1")));True(lines.Any(x=>x.Contains("unable to decide -> NeedsReview")));True(lines.Any(x=>x.Contains("unavailable - Quota")));True(lines.Any(x=>x.Contains("disabled for remainder of run")));
    }
    static void LlmBatchAndStatusSummary()
    {
        var f=Fixture();var workspace=ProgressWorkspace(f);var output=new StringWriter();var original=Console.Out;
        try
        {
            Console.SetOut(output);
            Equal(0,Mp3Organizer.Program.Run(["scan",f.Source,"--workspace",workspace]));
            Equal(0,Mp3Organizer.Program.Run(["analyze","--limit","10","--offline","--workspace",workspace]));
            True(output.ToString().Contains("Batch LLM activity:"));True(output.ToString().Contains("LLM requests:        0"));True(output.ToString().Contains("Deterministic only:  1"));
            output.GetStringBuilder().Clear();Equal(0,Mp3Organizer.Program.Run(["status","--workspace",workspace]));True(output.ToString().Contains("Cumulative LLM activity"));True(output.ToString().Contains("Deterministic only:  1"));
            output.GetStringBuilder().Clear();Equal(0,Mp3Organizer.Program.Run(["analyze","--limit","10","--offline","--workspace",workspace]));True(output.ToString().Contains("Deterministic only:  0"));
        }
        finally{Console.SetOut(original);}
        using var repo=new ProgressRepository(workspace);Equal(1L,repo.LlmTotals().DeterministicOnly);
    }
}
