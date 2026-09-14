using NuvexaDB.Samples;

if (args is ["--complex", ..] or ["--complex-v1", ..])
{
    var format1 = args[0] == "--complex-v1";
    var dest = args.Length > 1
        ? args[1]
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            format1 ? "nuvexa-complex-v1.nvx" : "nuvexa-complex.nvx");
    Console.Write(await ComplexCommerceDb.GenerateAsync(dest, format1));
    return;
}

var path = Path.Combine(AppContext.BaseDirectory, "sample.nvx");
Console.Write(await SampleTour.RunAsync(path));
