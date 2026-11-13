using System;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class AiAssistantServiceTests
{
    [TestMethod]
    public void ExtractJsonFromResponse_MarkdownFencedJson_ExtractsInnerJson()
    {
        string input = "```json\n[\"Movie 1\", \"Movie 2\"]\n```";
        string extracted = AiAssistantService.ExtractJsonFromResponse(input);

        Assert.AreEqual("[\"Movie 1\", \"Movie 2\"]", extracted);
        var parsed = JsonSerializer.Deserialize<List<string>>(extracted);
        Assert.IsNotNull(parsed);
        Assert.AreEqual(2, parsed.Count);
    }

    [TestMethod]
    public void ExtractJsonFromResponse_ConversationalWrapper_ExtractsJsonArray()
    {
        string input = "Here are the recommended movies:\n[\"Blade Runner 2049\", \"Matrix\"]\nLet me know if you need more!";
        string extracted = AiAssistantService.ExtractJsonFromResponse(input);

        Assert.AreEqual("[\"Blade Runner 2049\", \"Matrix\"]", extracted);
        var parsed = JsonSerializer.Deserialize<List<string>>(extracted);
        Assert.IsNotNull(parsed);
        Assert.AreEqual(2, parsed.Count);
    }

    [TestMethod]
    public void ExtractJsonFromResponse_JsonObject_ExtractsObject()
    {
        string input = "Result: {\"status\": \"ok\", \"count\": 42}";
        string extracted = AiAssistantService.ExtractJsonFromResponse(input);

        Assert.AreEqual("{\"status\": \"ok\", \"count\": 42}", extracted);
    }

    [TestMethod]
    public void ExtractJsonFromResponse_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, AiAssistantService.ExtractJsonFromResponse(""));
        Assert.AreEqual(string.Empty, AiAssistantService.ExtractJsonFromResponse("   "));
    }
}
