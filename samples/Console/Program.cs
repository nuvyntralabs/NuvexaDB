using NuvexaDB.Samples;

var path = Path.Combine(AppContext.BaseDirectory, "sample.nvx");
Console.Write(await SampleTour.RunAsync(path));
