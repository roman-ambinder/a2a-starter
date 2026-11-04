using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Ambinder.A2A.Compat.V0_3.Shared.Json;
using Ambinder.A2A.Compat.V0_3.Shared.Models;

namespace Ambinder.A2A.Compat.V0_3.Tests.Json;

/// <summary>
/// Tests for the 0.3 models and V0_3JsonContext. Examples are copied from the vendored v0.3.0 files
/// (src/Compat.V0_3.Shared/spec/v0.3.0); each test names the section. Where a test input is not a spec example it says
/// where the shape comes from.
/// </summary>
public class V03JsonTests
{

    // ---------- helpers ----------

    private static T Read<T>(string json, JsonTypeInfo<T> info) => JsonSerializer.Deserialize(json, info)!;

    private static string Write<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    /// <summary>
    /// Round trip: read the example, write it, compare to the example. The writer adds the schema's required
    /// "kind" where an example leaves it out (the spec's own history messages have none); nothing else may differ.
    /// </summary>
    private static T RoundTrip<T>(string example, JsonTypeInfo<T> info)
    {
        T value = Read(example, info);
        string written = Write(value, info);
        AssertSame(JsonNode.Parse(example), JsonNode.Parse(written), "$");
        return value;
    }

    private static void AssertSame(JsonNode? expected, JsonNode? actual, string path)
    {
        switch (expected)
        {
            case JsonObject eo:
                var ao = Assert.IsType<JsonObject>(actual);
                foreach (var (name, value) in eo)
                {
                    Assert.True(ao.ContainsKey(name), $"{path}.{name} is missing from the output");
                    AssertSame(value, ao[name], $"{path}.{name}");
                }

                foreach (var (name, _) in ao)
                {
                    Assert.True(eo.ContainsKey(name) || name == "kind", $"{path}.{name} was added by the writer");
                }

                break;
            case JsonArray ea:
                var aa = Assert.IsType<JsonArray>(actual);
                Assert.True(ea.Count == aa.Count, $"{path} has {aa.Count} items, expected {ea.Count}");
                for (int i = 0; i < ea.Count; i++)
                {
                    AssertSame(ea[i], aa[i], $"{path}[{i}]");
                }

                break;
            default:
                Assert.Equal(expected?.ToJsonString(), actual?.ToJsonString());
                break;
        }
    }

    private static readonly V0_3JsonContext Ctx = V0_3JsonContext.Default;

    // ---------- round trips of spec examples ----------

    // specification.md 9.2 "Basic Execution", step 2: the task result (result member of the response).
    [Fact]
    public void Task_round_trips_the_9_2_example()
    {
        var task = RoundTrip("""
            {
              "id": "363422be-b0f9-4692-a24d-278670e7c7f1",
              "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4",
              "status": { "state": "completed" },
              "artifacts": [
                {
                  "artifactId": "9b6934dd-37e3-4eb1-8766-962efaab63a1",
                  "name": "joke",
                  "parts": [ { "kind": "text", "text": "Why did the chicken cross the road? To get to the other side!" } ]
                }
              ],
              "history": [
                {
                  "role": "user",
                  "parts": [ { "kind": "text", "text": "tell me a joke" } ],
                  "messageId": "9229e770-767c-417b-a0b0-f0741243c589",
                  "taskId": "363422be-b0f9-4692-a24d-278670e7c7f1",
                  "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4"
                }
              ],
              "kind": "task",
              "metadata": {}
            }
            """, Ctx.V03Task);

        Assert.Equal("completed", task.Status.State);
        Assert.Equal("user", task.History![0].Role);
        Assert.Equal("Why did the chicken cross the road? To get to the other side!",
            Assert.IsType<V03TextPart>(task.Artifacts![0].Parts[0]).Text);
    }

    // specification.md 9.2, "responds quickly without a task": a message result. The example has no "role".
    [Fact]
    public void Message_round_trips_the_9_2_example()
    {
        var message = RoundTrip("""
            {
              "messageId": "363422be-b0f9-4692-a24d-278670e7c7f1",
              "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4",
              "parts": [ { "kind": "text", "text": "Why did the chicken cross the road? To get to the other side!" } ],
              "kind": "message",
              "metadata": {}
            }
            """, Ctx.V03Message);

        Assert.Null(message.Role);
    }

    // specification.md 9.4 step 4: input-required history, status message, timestamp, data part in an artifact.
    [Fact]
    public void Task_round_trips_the_9_4_example_with_data_part_and_status_message()
    {
        var task = RoundTrip("""
            {
              "id": "3f36680c-7f37-4a5f-945e-d78981fafd36",
              "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4",
              "status": {
                "state": "input-required",
                "message": {
                  "role": "agent",
                  "parts": [ { "kind": "text", "text": "Sure, I can help with that! Where would you like to fly to, and from where?" } ],
                  "messageId": "c2e1b2dd-f200-4b04-bc22-1b0c65a1aad2",
                  "taskId": "3f36680c-7f37-4a5f-945e-d78981fafd36",
                  "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4"
                },
                "timestamp": "2024-03-15T10:10:00Z"
              },
              "artifacts": [
                {
                  "artifactId": "9b6934dd-37e3-4eb1-8766-962efaab63a1",
                  "name": "FlightItinerary.json",
                  "parts": [
                    { "kind": "data", "data": { "confirmationId": "XYZ123", "from": "JFK", "to": "LHR", "returnDeparture": "..." } }
                  ]
                }
              ],
              "kind": "task"
            }
            """, Ctx.V03Task);

        Assert.Equal("input-required", task.Status.State);
        Assert.Equal("2024-03-15T10:10:00Z", task.Status.Timestamp);
        var data = Assert.IsType<V03DataPart>(task.Artifacts![0].Parts[0]);
        Assert.Equal("XYZ123", data.Data["confirmationId"].GetString());
    }

    // specification.md 9.3 events 2 and 5 (artifact-update with append / lastChunk, status-update with final).
    [Fact]
    public void Stream_events_round_trip_the_9_3_examples()
    {
        var artifact = RoundTrip("""
            {
              "taskId": "225d6247-06ba-4cda-a08b-33ae35c8dcfa",
              "contextId": "05217e44-7e9f-473e-ab4f-2c2dde50a2b1",
              "artifact": {
                "artifactId": "9b6934dd-37e3-4eb1-8766-962efaab63a1",
                "parts": [ { "kind": "text", "text": "<section 2...>" } ]
              },
              "append": true,
              "lastChunk": false,
              "kind": "artifact-update"
            }
            """, Ctx.IV03StreamEvent);
        var update = Assert.IsType<V03TaskArtifactUpdateEvent>(artifact);
        Assert.True(update.Append);
        Assert.False(update.LastChunk);

        var status = RoundTrip("""
            {
              "taskId": "225d6247-06ba-4cda-a08b-33ae35c8dcfa",
              "contextId": "05217e44-7e9f-473e-ab4f-2c2dde50a2b1",
              "status": { "state": "completed", "timestamp": "2025-04-02T16:59:35.331844" },
              "final": true,
              "kind": "status-update"
            }
            """, Ctx.IV03StreamEvent);
        Assert.True(Assert.IsType<V03TaskStatusUpdateEvent>(status).Final);
    }

    // specification.md 9.3 event 1: the first event of a stream is a task. Read through the stream converter.
    [Fact]
    public void Stream_event_task_is_read_as_a_task()
    {
        var task = Read("""
            { "id": "225d6247", "contextId": "05217e44", "status": { "state": "submitted", "timestamp": "2025-04-02T16:59:25.331844" }, "kind": "task", "metadata": {} }
            """, Ctx.IV03StreamEvent);
        Assert.Equal("submitted", Assert.IsType<V03Task>(task).Status.State);
    }

    // specification.md 9.6 steps 1 and 2: file with bytes (request), file with uri (artifact).
    [Fact]
    public void File_parts_round_trip_the_9_6_examples()
    {
        var sent = RoundTrip("""
            {
              "message": {
                "role": "user",
                "parts": [
                  { "kind": "text", "text": "Analyze this image and highlight any faces." },
                  { "kind": "file", "file": { "name": "input_image.png", "mimeType": "image/png", "bytes": "iVBORw0KGgoAAAANSUhEUgAAAAUA..." } }
                ],
                "messageId": "6dbc13b5-bd57-4c2b-b503-24e381b6c8d6"
              }
            }
            """, Ctx.V03MessageSendParams);
        var file = Assert.IsType<V03FilePart>(sent.Message.Parts[1]).File;
        Assert.Equal("input_image.png", file.Name);
        Assert.Equal("iVBORw0KGgoAAAANSUhEUgAAAAUA...", file.Bytes);
        Assert.Null(file.Uri);

        var artifact = RoundTrip("""
            {
              "artifactId": "9b6934dd-37e3-4eb1-8766-962efaab63a1",
              "name": "processed_image_with_faces.png",
              "parts": [
                { "kind": "file", "file": { "name": "output.png", "mimeType": "image/png", "uri": "https://storage.example.com/processed/task-bbb/output.png?token=xyz" } }
              ]
            }
            """, Ctx.V03Artifact);
        var uri = Assert.IsType<V03FilePart>(artifact.Parts[0]).File;
        Assert.Equal("https://storage.example.com/processed/task-bbb/output.png?token=xyz", uri.Uri);
        Assert.Null(uri.Bytes);
    }

    // specification.md 9.4 step 3 (blocking) and 9.5 step 1 (pushNotificationConfig with authentication.schemes).
    [Fact]
    public void Send_params_round_trip_the_9_4_and_9_5_examples()
    {
        var blocking = RoundTrip("""
            {
              "message": {
                "role": "user",
                "parts": [ { "kind": "text", "text": "I want to fly from New York (JFK) to London (LHR)." } ],
                "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4",
                "taskId": "3f36680c-7f37-4a5f-945e-d78981fafd36",
                "messageId": "0db1d6c4-3976-40ed-b9b8-0043ea7a03d3"
              },
              "configuration": { "blocking": true }
            }
            """, Ctx.V03MessageSendParams);
        Assert.True(blocking.Configuration!.Blocking);

        var push = RoundTrip("""
            {
              "message": {
                "role": "user",
                "parts": [ { "kind": "text", "text": "Generate the Q1 sales report." } ],
                "messageId": "6dbc13b5-bd57-4c2b-b503-24e381b6c8d6"
              },
              "configuration": {
                "pushNotificationConfig": {
                  "url": "https://client.example.com/webhook/a2a-notifications",
                  "token": "secure-client-token-for-task-aaa",
                  "authentication": { "schemes": ["Bearer"] }
                }
              }
            }
            """, Ctx.V03MessageSendParams);
        Assert.Equal("secure-client-token-for-task-aaa", push.Configuration!.PushNotificationConfig!.Token);
        Assert.Equal(["Bearer"], push.Configuration.PushNotificationConfig.Authentication!.Schemes);
    }

    // a2a.json TaskPushNotificationConfig (taskId + pushNotificationConfig) and PushNotificationAuthenticationInfo.credentials.
    // The values are made up; the member names are from the schema.
    [Fact]
    public void Task_push_notification_config_round_trips()
    {
        var config = RoundTrip("""
            {
              "taskId": "task-1",
              "pushNotificationConfig": { "id": "cfg-1", "url": "https://example.com/hook", "token": "t", "authentication": { "schemes": ["Bearer"], "credentials": "c" } }
            }
            """, Ctx.V03TaskPushNotificationConfig);
        Assert.Equal("task-1", config.TaskId);
        Assert.Equal("cfg-1", config.PushNotificationConfig.Id);
    }

    // specification.md 5.7 "Sample Agent Card" (copied whole; its protocolVersion is 0.2.9, which must be kept as is).
    [Fact]
    public void Agent_card_round_trips_the_5_7_sample()
    {
        var card = RoundTrip("""
            {
              "protocolVersion": "0.2.9",
              "name": "GeoSpatial Route Planner Agent",
              "description": "Provides advanced route planning, traffic analysis, and custom map generation services. This agent can calculate optimal routes, estimate travel times considering real-time traffic, and create personalized maps with points of interest.",
              "url": "https://georoute-agent.example.com/a2a/v1",
              "preferredTransport": "JSONRPC",
              "additionalInterfaces" : [
                {"url": "https://georoute-agent.example.com/a2a/v1", "transport": "JSONRPC"},
                {"url": "https://georoute-agent.example.com/a2a/grpc", "transport": "GRPC"},
                {"url": "https://georoute-agent.example.com/a2a/json", "transport": "HTTP+JSON"}
              ],
              "provider": {
                "organization": "Example Geo Services Inc.",
                "url": "https://www.examplegeoservices.com"
              },
              "iconUrl": "https://georoute-agent.example.com/icon.png",
              "version": "1.2.0",
              "documentationUrl": "https://docs.examplegeoservices.com/georoute-agent/api",
              "capabilities": {
                "streaming": true,
                "pushNotifications": true,
                "stateTransitionHistory": false
              },
              "securitySchemes": {
                "google": {
                  "type": "openIdConnect",
                  "openIdConnectUrl": "https://accounts.google.com/.well-known/openid-configuration"
                }
              },
              "security": [{ "google": ["openid", "profile", "email"] }],
              "defaultInputModes": ["application/json", "text/plain"],
              "defaultOutputModes": ["application/json", "image/png"],
              "skills": [
                {
                  "id": "route-optimizer-traffic",
                  "name": "Traffic-Aware Route Optimizer",
                  "description": "Calculates the optimal driving route between two or more locations, taking into account real-time traffic conditions, road closures, and user preferences (e.g., avoid tolls, prefer highways).",
                  "tags": ["maps", "routing", "navigation", "directions", "traffic"],
                  "examples": [
                    "Plan a route from '1600 Amphitheatre Parkway, Mountain View, CA' to 'San Francisco International Airport' avoiding tolls.",
                    "{\"origin\": {\"lat\": 37.422, \"lng\": -122.084}, \"destination\": {\"lat\": 37.7749, \"lng\": -122.4194}, \"preferences\": [\"avoid_ferries\"]}"
                  ],
                  "inputModes": ["application/json", "text/plain"],
                  "outputModes": [
                    "application/json",
                    "application/vnd.geo+json",
                    "text/html"
                  ]
                },
                {
                  "id": "custom-map-generator",
                  "name": "Personalized Map Generator",
                  "description": "Creates custom map images or interactive map views based on user-defined points of interest, routes, and style preferences. Can overlay data layers.",
                  "tags": ["maps", "customization", "visualization", "cartography"],
                  "examples": [
                    "Generate a map of my upcoming road trip with all planned stops highlighted.",
                    "Show me a map visualizing all coffee shops within a 1-mile radius of my current location."
                  ],
                  "inputModes": ["application/json"],
                  "outputModes": [
                    "image/png",
                    "image/jpeg",
                    "application/json",
                    "text/html"
                  ]
                }
              ],
              "supportsAuthenticatedExtendedCard": true,
              "signatures": [
                {
                  "protected": "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSIsImprdSI6Imh0dHBzOi8vZXhhbXBsZS5jb20vYWdlbnQvandrcy5qc29uIn0",
                  "signature": "QFdkNLNszlGj3z3u0YQGt_T9LixY3qtdQpZmsTdDHDe3fXV9y9-B3m2-XgCpzuhiLt8E0tV6HXoZKHv4GtHgKQ"
                }
              ]
            }
            """, Ctx.V03AgentCard);

        Assert.Equal("0.2.9", card.ProtocolVersion);
        Assert.Equal("JSONRPC", card.PreferredTransport);
        Assert.Equal(3, card.AdditionalInterfaces!.Count);
        Assert.Equal("openIdConnect", card.SecuritySchemes!["google"].Type);
        Assert.Equal(["openid", "profile", "email"], card.Security![0]["google"]);
        Assert.False(card.Capabilities.StateTransitionHistory);
        Assert.True(card.SupportsAuthenticatedExtendedCard);
        Assert.Equal(2, card.Skills.Count);
    }

    // a2a.json JSONRPCError / TaskNotFoundError (code -32001, default message "Task not found"); the data value is made up.
    [Fact]
    public void Json_rpc_error_round_trips_with_structured_data()
    {
        var error = RoundTrip("""{ "code": -32001, "message": "Task not found", "data": { "taskId": "t-1" } }""", Ctx.V03JsonRpcError);
        Assert.Equal(-32001, error.Code);
        Assert.Equal("t-1", error.Data!.Value.GetProperty("taskId").GetString());
    }

    // a2a.json OAuthFlows / OAuth2SecurityScheme / APIKeySecurityScheme / HTTPAuthSecurityScheme. Values are made up.
    [Fact]
    public void Security_schemes_round_trip()
    {
        var card = RoundTrip("""
            {
              "protocolVersion": "0.3.0", "name": "a", "description": "d", "url": "https://a.example/rpc", "version": "1",
              "capabilities": { "streaming": true, "extensions": [ { "uri": "https://x.example/ext", "description": "e", "required": true, "params": { "k": 1 } } ] },
              "securitySchemes": {
                "o": { "type": "oauth2", "flows": { "authorizationCode": { "authorizationUrl": "https://a/auth", "tokenUrl": "https://a/token", "scopes": { "read": "Read" } } }, "oauth2MetadataUrl": "https://a/meta" },
                "k": { "type": "apiKey", "in": "header", "name": "X-Key" },
                "b": { "type": "http", "scheme": "bearer", "bearerFormat": "JWT" },
                "m": { "type": "mutualTLS" }
              },
              "defaultInputModes": ["text/plain"], "defaultOutputModes": ["text/plain"], "skills": []
            }
            """, Ctx.V03AgentCard);
        Assert.Equal("header", card.SecuritySchemes!["k"].In);
        Assert.Equal("Read", card.SecuritySchemes["o"].Flows!.AuthorizationCode!.Scopes["read"]);
        Assert.True(card.Capabilities.Extensions![0].Required);
    }

    // ---------- writing ----------

    [Fact]
    public void Writers_put_kind_first()
    {
        var task = new V03Task { Id = "t", ContextId = "c", Status = new V03TaskStatus { State = "working" } };
        Assert.StartsWith("""{"kind":"task","id":"t",""", Write(task, Ctx.V03Task));
        Assert.StartsWith("""{"kind":"task",""", Write<IV03Result>(task, Ctx.IV03Result));
        Assert.StartsWith("""{"kind":"message",""", Write(new V03Message { MessageId = "m" }, Ctx.V03Message));
        Assert.StartsWith("""{"kind":"status-update",""", Write<IV03StreamEvent>(new V03TaskStatusUpdateEvent { TaskId = "t" }, Ctx.IV03StreamEvent));
        Assert.StartsWith("""{"kind":"artifact-update",""", Write<IV03StreamEvent>(new V03TaskArtifactUpdateEvent { TaskId = "t" }, Ctx.IV03StreamEvent));
        Assert.Equal("""{"kind":"text","text":"hi"}""", Write<V03Part>(new V03TextPart("hi"), Ctx.V03Part));
        Assert.StartsWith("""{"kind":"file","file":{"name":"n","bytes":"AA=="}""", Write<V03Part>(new V03FilePart(new V03FileContent { Name = "n", Bytes = "AA==" }), Ctx.V03Part));
    }

    [Fact]
    public void Writers_leave_out_null_members_and_the_read_only_aliases()
    {
        string message = Write(new V03Message { MessageId = "m", Parts = [new V03TextPart("hi")] }, Ctx.V03Message);
        Assert.Equal("""{"kind":"message","parts":[{"kind":"text","text":"hi"}],"messageId":"m"}""", message);
        Assert.DoesNotContain("content", message);

        string configuration = Write(new V03MessageSendConfiguration { Blocking = false }, Ctx.V03MessageSendConfiguration);
        Assert.Equal("""{"blocking":false}""", configuration);
    }

    // ---------- tolerant read ----------

    [Fact]
    public void Text_part_is_read_with_or_without_kind()
    {
        // The second form is the ProtoJSON shape of a2a.proto Part { string text = 1; } (not a spec example).
        Assert.Equal("hi", Assert.IsType<V03TextPart>(Read("""{"kind":"text","text":"hi"}""", Ctx.V03Part)).Text);
        Assert.Equal("hi", Assert.IsType<V03TextPart>(Read("""{"text":"hi"}""", Ctx.V03Part)).Text);
    }

    [Fact]
    public void Kind_may_come_after_other_members()
    {
        var part = Read("""{"text":"hi","metadata":{"a":1},"kind":"text"}""", Ctx.V03Part);
        Assert.Equal("hi", Assert.IsType<V03TextPart>(part).Text);
        Assert.Equal(1, part.Metadata!["a"].GetInt32());

        var task = Read("""{"status":{"state":"working"},"id":"t","contextId":"c","kind":"task"}""", Ctx.IV03Result);
        Assert.Equal("t", Assert.IsType<V03Task>(task).Id);

        var data = Read("""{"data":{"a":1},"metadata":{"m":true},"kind":"data"}""", Ctx.V03Part);
        Assert.Equal(1, Assert.IsType<V03DataPart>(data).Data["a"].GetInt32());
    }

    [Fact]
    public void Kind_may_be_absent_where_the_shape_implies_the_type()
    {
        // Not spec examples: the member sets come from a2a.json (Task, Message, TaskStatusUpdateEvent, TaskArtifactUpdateEvent).
        Assert.IsType<V03Task>(Read("""{"id":"t","contextId":"c","status":{"state":"working"}}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03Message>(Read("""{"messageId":"m","role":"agent","parts":[{"text":"x"}]}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03Message>(Read("""{"messageId":"m","role":"agent","parts":[{"text":"x"}]}""", Ctx.IV03Result));
        Assert.IsType<V03TaskStatusUpdateEvent>(Read("""{"taskId":"t","contextId":"c","status":{"state":"working"},"final":false}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03TaskArtifactUpdateEvent>(Read("""{"taskId":"t","contextId":"c","artifact":{"artifactId":"a","parts":[]}}""", Ctx.IV03StreamEvent));
    }

    [Fact]
    public void File_content_accepts_the_protojson_spellings()
    {
        // Member names from a2a.proto FilePart (file_with_uri, file_with_bytes, mime_type) in ProtoJSON camelCase and original form.
        var uri = Assert.IsType<V03FilePart>(Read("""{"file":{"fileWithUri":"https://e.example/a.png","mimeType":"image/png"}}""", Ctx.V03Part)).File;
        Assert.Equal("https://e.example/a.png", uri.Uri);
        Assert.Equal("image/png", uri.MimeType);

        var bytes = Assert.IsType<V03FilePart>(Read("""{"file":{"fileWithBytes":"AAEC","mimeType":"application/octet-stream"}}""", Ctx.V03Part)).File;
        Assert.Equal("AAEC", bytes.Bytes);

        var snake = Read("""{"file_with_uri":"https://e.example/b","mime_type":"text/plain"}""", Ctx.V03FileContent);
        Assert.Equal("https://e.example/b", snake.Uri);
        Assert.Equal("text/plain", snake.MimeType);
        Assert.Equal("AAEC", Read("""{"file_with_bytes":"AAEC"}""", Ctx.V03FileContent).Bytes);
    }

    [Fact]
    public void File_content_without_bytes_or_uri_is_rejected()
    {
        // specification.md 9.3 and 9.2 show a file with "data": "<base64-encoded-content>". a2a.json FileWithBytes names the member "bytes",
        // so "data" is not read and the file has no content: that is an error, not a silent empty file.
        Assert.Throws<JsonException>(() => Read("""{"kind":"file","file":{"mimeType":"image/png","data":"<base64-encoded-content>"}}""", Ctx.V03Part));
    }

    [Fact]
    public void Protojson_data_part_is_unwrapped_only_when_kind_is_absent()
    {
        // a2a.proto Part { DataPart data = 3; } and DataPart { Struct data = 1; } give {"data":{"data":{...}}} in ProtoJSON (not a spec example).
        var wrapped = Assert.IsType<V03DataPart>(Read("""{"data":{"data":{"a":1}}}""", Ctx.V03Part));
        Assert.Equal(1, wrapped.Data["a"].GetInt32());

        // With kind, the schema form applies: the payload is exactly what is in "data", even if it has a "data" key.
        var plain = Assert.IsType<V03DataPart>(Read("""{"kind":"data","data":{"data":{"a":1}}}""", Ctx.V03Part));
        Assert.Equal(JsonValueKind.Object, plain.Data["data"].ValueKind);
    }

    [Fact]
    public void Message_content_is_read_as_parts_and_push_notification_as_push_notification_config()
    {
        // a2a.proto Message.content and SendMessageConfiguration.push_notification (ProtoJSON pushNotification).
        var message = Read("""{"messageId":"m","role":"ROLE_USER","content":[{"text":"hi"}]}""", Ctx.V03Message);
        Assert.Equal("hi", Assert.IsType<V03TextPart>(Assert.Single(message.Parts)).Text);
        Assert.Equal("ROLE_USER", message.Role);

        var configuration = Read("""{"pushNotification":{"url":"https://e.example/h"},"historyLength":3}""", Ctx.V03MessageSendConfiguration);
        Assert.Equal("https://e.example/h", configuration.PushNotificationConfig!.Url);
        Assert.Equal(3, configuration.HistoryLength);
    }

    [Fact]
    public void Task_push_notification_config_accepts_the_proto_name_member()
    {
        // a2a.proto TaskPushNotificationConfig.name = "tasks/{id}/pushNotificationConfigs/{id}".
        var config = Read("""{"name":"tasks/t1/pushNotificationConfigs/c1","pushNotificationConfig":{"url":"https://e.example/h"}}""", Ctx.V03TaskPushNotificationConfig);
        Assert.Equal("tasks/t1/pushNotificationConfigs/c1", config.Name);
        Assert.Null(config.TaskId);
    }

    [Fact]
    public void Stream_response_wrappers_are_unwrapped()
    {
        // a2a.proto StreamResponse { task, msg (json_name "message"), status_update, artifact_update }; spec text 7.9 lists
        // { message?, task?, statusUpdate?, artifactUpdate? } for the REST stream.
        Assert.IsType<V03Task>(Read("""{"task":{"id":"t","contextId":"c","status":{"state":"working"}}}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03Message>(Read("""{"message":{"messageId":"m","parts":[]}}""", Ctx.IV03StreamEvent));
        var status = Read("""{"statusUpdate":{"taskId":"t","contextId":"c","status":{"state":"completed"},"final":true}}""", Ctx.IV03StreamEvent);
        Assert.True(Assert.IsType<V03TaskStatusUpdateEvent>(status).Final);
        Assert.IsType<V03TaskStatusUpdateEvent>(Read("""{"status_update":{"taskId":"t","contextId":"c","status":{"state":"completed"},"final":true}}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03TaskArtifactUpdateEvent>(Read("""{"artifactUpdate":{"taskId":"t","contextId":"c","artifact":{"artifactId":"a","parts":[]}}}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03TaskArtifactUpdateEvent>(Read("""{"artifact_update":{"taskId":"t","contextId":"c","artifact":{"artifactId":"a","parts":[]}}}""", Ctx.IV03StreamEvent));
        Assert.IsType<V03Message>(Read("""{"msg":{"messageId":"m","parts":[]}}""", Ctx.IV03StreamEvent));

        // The send result wrapper (a2a.proto SendMessageResponse { task | msg }) goes through the result converter.
        Assert.IsType<V03Task>(Read("""{"task":{"id":"t","contextId":"c","status":{"state":"working"}}}""", Ctx.IV03Result));
        Assert.IsType<V03Message>(Read("""{"message":{"messageId":"m","parts":[]}}""", Ctx.IV03Result));
    }

    [Fact]
    public void Unknown_members_are_ignored()
    {
        var part = Read("""{"kind":"text","text":"hi","future":{"a":[1,2,{"b":null}]},"x":1}""", Ctx.V03Part);
        Assert.Equal("hi", Assert.IsType<V03TextPart>(part).Text);

        var task = Read("""{"kind":"task","id":"t","contextId":"c","status":{"state":"working","extra":[1]},"future":"x"}""", Ctx.IV03Result);
        Assert.Equal("t", Assert.IsType<V03Task>(task).Id);

        var file = Read("""{"name":"n","uri":"u","extra":{"a":1}}""", Ctx.V03FileContent);
        Assert.Equal("u", file.Uri);

        var wrapped = Read("""{"other":{"a":1},"task":{"id":"t","contextId":"c","status":{"state":"working"}}}""", Ctx.IV03StreamEvent);
        Assert.IsType<V03Task>(wrapped);
    }

    // ---------- unknown kind and bad shapes ----------

    [Theory]
    [InlineData("""{"kind":"image","text":"hi"}""")]
    [InlineData("""{"text":"hi","kind":"Text"}""")]
    [InlineData("""{"kind":7,"text":"hi"}""")]
    [InlineData("""{"foo":1}""")]
    [InlineData("""[]""")]
    public void Part_with_unknown_kind_or_unknown_shape_throws_JsonException(string json)
        => Assert.Throws<JsonException>(() => Read(json, Ctx.V03Part));

    [Theory]
    [InlineData("""{"kind":"status-update","taskId":"t","contextId":"c","status":{"state":"working"},"final":false}""")]
    [InlineData("""{"kind":"artifact-update","taskId":"t","contextId":"c","artifact":{"artifactId":"a","parts":[]}}""")]
    [InlineData("""{"kind":"widget","id":"t"}""")]
    [InlineData("""{"kind":null}""")]
    [InlineData("""{"statusUpdate":{"taskId":"t","contextId":"c","status":{"state":"working"},"final":false}}""")]
    public void Result_with_unknown_kind_or_an_event_throws_JsonException(string json)
        => Assert.Throws<JsonException>(() => Read(json, Ctx.IV03Result));

    [Theory]
    [InlineData("""{"kind":"widget","id":"t"}""")]
    [InlineData("""{"kind":"Task","id":"t"}""")]
    [InlineData("""{"foo":"bar"}""")]
    public void Stream_event_with_unknown_kind_throws_JsonException(string json)
        => Assert.Throws<JsonException>(() => Read(json, Ctx.IV03StreamEvent));

    [Fact]
    public void Unknown_kind_nested_in_a_task_throws_JsonException()
        => Assert.Throws<JsonException>(() => Read(
            """{"kind":"task","id":"t","contextId":"c","status":{"state":"working"},"artifacts":[{"artifactId":"a","parts":[{"kind":"video","uri":"u"}]}]}""",
            Ctx.IV03Result));
}
