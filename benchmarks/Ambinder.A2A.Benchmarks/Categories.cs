namespace Ambinder.A2A.Benchmarks;

internal static class Categories
{
    public const string Json = "Json";
    public const string Grpc = "Grpc";
    public const string Request = "Request";
    public const string Stream = "Stream";
    public const string Pipeline = "Pipeline";
    public const string Store = "Store";
    public const string Db = "Db";       // needs Docker; nightly weekly and on request
    public const string Push = "Push";
    public const string Authoring = "Authoring"; // package 11 scenarios (11 plan Task 24); review item C
    public const string Client = "Client";
}
