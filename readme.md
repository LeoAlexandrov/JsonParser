# Usage

Add `JsonParser.cs` to your project and reference `AleProjects.Json` namespace in your code.

## Examples

```
using AleProjects.Json;

...

string json = @"{
	""User"": { ""Name"": ""Leo"", ""Role"": ""Admin"", ""Enabled"": true, ""LastLogon"": ""\/Date(1614929892344)\/""}, 
	""Array1"": [0, 1, 2, 3, 4, 5], // this is comment
	""Array2"": [0, 1, 2.1, 3.112, 0.342],
/*
This is another comment
*/
	""Array3"": [""\/Date(1614929892344)\/""], 
	""Array4"": [""\/Date(1614929892344)\/"", ""QWERTY""],
	""Array5"": [ { ""x"": 1, ""y"": 2 }, { ""x"": 3, ""y"": 4 } ]
	""IntValue"": 1,
	""LongValue"": 8589934592,
	""DoubleValue"": 3.1416,
	""BoolValue"": true,
	""NullValue"": null,
	""DateTimeAlt"": ""2009-06-15T13:45:30.000Z""
}";


JsonDoc doc;

try
{
	doc = JsonDoc.Parse(json, new JsonDoc.ParsingSettings() { AllowComments = true, RecognizeDateTime = true, ForceDoubleInArrays = false, StrictPropertyNames = true });
}
catch (JsonParseException ex)
{
	System.Diagnostics.Debug.WriteLine("Error message: {0}, code {1}, line {2}, position {3}", ex.Message, ex.Data["Code"], ex.Data["Line"], ex.Data["Position"]);
}
catch
{
}


if (doc.Root is JsonDoc.JsonObject root)
{
	// access properties by path

	var userName = root.GetValueOrDefault<string>("User", "Name"); 
	var lastLogon = root.GetValueOrDefault<DateTime>("User", "LastLogon");
	var x = root.GetValueOrDefault<int>("Array5", "1", "x"); // x will be 3
	
	var array1 = root.GetValueOrDefault<IList<int>>("Array1");
	var array2 = root.GetValueOrDefault<IList<double>>("Array2");
	var array3 = root.GetValueOrDefault<IList<DateTime>>("Array3");
	var array4 = root.GetValueOrDefault<IList<object>>("Array4");

	array3.Add(DateTime.Now); // parsed object is not immutable and can be modified
}

```

_To be continued ..._