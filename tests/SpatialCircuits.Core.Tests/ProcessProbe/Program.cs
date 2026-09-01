using SpatialCircuits.Core;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: identity-probe <document.json>");
    return 2;
}

var document = CircuitDocumentCodec.Read(File.ReadAllBytes(args[0]));
var definition = document.Definitions.Single();
var component = document.Components.Single();
var port = definition.Ports.First();
Console.WriteLine(
    $"document={document.DocumentId.Value};" +
    $"definition={definition.Id.Value};" +
    $"component={component.Id.Value};" +
    $"port={port.Id.Value}@{port.Location.X},{port.Location.Y};" +
    $"behavior={definition.BehaviorId.Value}");
return 0;
