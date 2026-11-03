using System.Text.Json;
using A2A.Protocol;
using A2A.Server.Agents;
using A2A.Server.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.Server.Tests.Tasks;

public class TaskManagerTests
{
    private static async Task<A2AException> ThrowsA2A(A2AErrorCode code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<A2AException>(action);
        Assert.Equal(code, ex.Code);
        return ex;
    }

    private static string TextOf(Message? m) => Assert.Single(m!.Parts).Text!;

    [Fact]
    public async Task Send_with_direct_reply_returns_message_and_stores_no_task()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.ReplyAsync(TestHost.AgentText("hello back"), ct);

        var response = await host.SendAsync(TestHost.Text("hello"));

        Assert.Null(response.Task);
        Assert.Equal("hello back", TextOf(response.Message));
        Assert.Equal(Role.Agent, response.Message!.Role);
        var list = await host.Manager.ListTasksAsync(TestHost.Call(), new ListTasksRequest(), CancellationToken.None);
        Assert.Empty(list.Tasks);
    }

    [Fact]
    public async Task Send_blocking_waits_for_completed_and_returns_artifacts()
    {
        var host = new TestHost();
        var entered = new Signal();
        var gate = new Gate();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            entered.Set();
            await gate.WaitAsync(ct);
            await sink.AddArtifactAsync(new Artifact { ArtifactId = "a1", Parts = [Part.FromText("part 1")] }, false, false, ct);
            await sink.AddArtifactAsync(new Artifact { ArtifactId = "a1", Parts = [Part.FromText("part 2")] }, true, true, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };

        var send = host.SendAsync(TestHost.Text("go"));
        await entered.WaitAsync();
        Assert.False(send.IsCompleted);
        gate.Open();
        var task = (await send).Task!;

        Assert.Equal(TaskState.Completed, task.Status.State);
        Assert.Equal(TestHost.Now, task.Status.Timestamp);
        var artifact = Assert.Single(task.Artifacts);
        Assert.Equal("a1", artifact.ArtifactId);
        Assert.Equal(["part 1", "part 2"], artifact.Parts.Select(p => p.Text));
        var user = Assert.Single(task.History);
        Assert.Equal(task.Id, user.TaskId);
        Assert.Equal(task.ContextId, user.ContextId);
        Assert.True(Guid.TryParse(task.Id, out _));
        Assert.True(Guid.TryParse(task.ContextId, out _));
    }

    [Fact]
    public async Task Send_uses_message_context_id_when_given()
    {
        var host = new TestHost();
        var request = TestHost.Text("go");
        request = request with { Message = request.Message with { ContextId = "ctx-1" } };

        var task = (await host.SendAsync(request)).Task!;

        Assert.Equal("ctx-1", task.ContextId);
    }

    [Theory]
    [InlineData(257)]
    [InlineData(-1)] // control character
    public async Task Unacceptable_client_context_id_is_rejected_not_replaced(int length)
    {
        var host = new TestHost();
        var contextId = length > 0 ? new string('c', length) : "ctx\n1";
        var request = TestHost.Text("go");
        request = request with { Message = request.Message with { ContextId = contextId } };

        var ex = await ThrowsA2A(A2AErrorCode.InvalidParams, () => host.SendAsync(request));
        Assert.Contains("contextId", ex.Message);
    }

    [Fact]
    public async Task Context_id_of_256_characters_is_kept()
    {
        var host = new TestHost();
        var contextId = new string('c', 256);
        var request = TestHost.Text("go");
        request = request with { Message = request.Message with { ContextId = contextId } };

        Assert.Equal(contextId, (await host.SendAsync(request)).Task!.ContextId);
    }

    [Fact]
    public async Task Send_return_immediately_returns_working_task()
    {
        var host = new TestHost();
        var gate = new Gate();
        var done = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await gate.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
            done.Set();
        };

        var task = (await host.SendAsync(TestHost.Text("go", returnImmediately: true))).Task!;

        Assert.Equal(TaskState.Working, task.Status.State);
        gate.Open();
        await done.WaitAsync();
        Assert.Equal(TaskState.Completed, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Send_blocking_returns_at_input_required()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.InputRequired, TestHost.AgentText("which city?"), ct);

        var task = (await host.SendAsync(TestHost.Text("book a flight"))).Task!;

        Assert.Equal(TaskState.InputRequired, task.Status.State);
        Assert.Equal("which city?", TextOf(task.Status.Message));
        Assert.Equal(task.Id, task.Status.Message!.TaskId);
        Assert.Equal(2, task.History.Count);
        Assert.Equal(Role.User, task.History[0].Role);
        Assert.Equal("which city?", TextOf(task.History[1]));
    }

    [Theory]
    [InlineData(TaskState.AuthRequired, "tenant:t1/app:b", true)]   // step-up within the tenant
    [InlineData(TaskState.AuthRequired, "tenant:t2/app:b", false)]  // other tenant
    [InlineData(TaskState.InputRequired, "tenant:t1/app:b", false)] // not a step-up
    public async Task Another_owner_may_continue_only_an_auth_required_task_of_the_same_tenant(TaskState paused, string caller, bool allowed)
    {
        var host = new TestHost();
        host.Scripts.Execute = (ctx, sink, ct) => ctx.CurrentTask is null
            ? sink.UpdateStatusAsync(paused, null, ct)
            : sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        var first = (await host.SendAsync(TestHost.Text("start"), owner: "tenant:t1/app:a")).Task!;

        if (allowed)
        {
            var done = (await host.SendAsync(TestHost.Text("go", first.Id), owner: caller)).Task!;
            Assert.Equal(TaskState.Completed, done.Status.State);
            // The task keeps its owner: the continuing app still cannot read it.
            await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.GetAsync(first.Id, owner: caller));
            Assert.Equal(TaskState.Completed, (await host.GetAsync(first.Id, owner: "tenant:t1/app:a")).Status.State);
        }
        else
        {
            await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.SendAsync(TestHost.Text("go", first.Id), owner: caller));
            Assert.Equal(paused, (await host.GetAsync(first.Id, owner: "tenant:t1/app:a")).Status.State);
        }
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.Manager.CancelTaskAsync(TestHost.Call(caller), new CancelTaskRequest { Id = first.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Continue_input_required_task_completes()
    {
        var host = new TestHost();
        host.Scripts.Execute = async (ctx, sink, ct) =>
        {
            if (ctx.CurrentTask is null)
            {
                await sink.UpdateStatusAsync(TaskState.InputRequired, TestHost.AgentText("which city?"), ct);
                return;
            }
            Assert.Equal(TaskState.Submitted, ctx.CurrentTask.Status.State);
            Assert.Equal("Paris", TextOf(ctx.Message));
            await sink.UpdateStatusAsync(TaskState.Completed, TestHost.AgentText("booked"), ct);
        };

        var first = (await host.SendAsync(TestHost.Text("book a flight"))).Task!;
        var second = (await host.SendAsync(TestHost.Text("Paris", taskId: first.Id))).Task!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(TaskState.Completed, second.Status.State);
        Assert.Equal(["book a flight", "which city?", "Paris", "booked"], second.History.Select(TextOf));
        Assert.Equal(first.Id, second.History[2].TaskId);
        Assert.Equal(first.ContextId, second.History[2].ContextId);
        Assert.Equal(first.Id, host.Scripts.Contexts.Last().TaskId);
    }

    [Fact]
    public async Task Continue_terminal_task_is_rejected()
    {
        var host = new TestHost();
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        await ThrowsA2A(A2AErrorCode.UnsupportedOperation, () => host.SendAsync(TestHost.Text("again", taskId: task.Id)));
    }

    [Fact]
    public async Task Continue_unknown_task_throws_task_not_found()
    {
        var host = new TestHost();

        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.SendAsync(TestHost.Text("hi", taskId: Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task Concurrent_continuation_second_is_rejected()
    {
        for (var i = 0; i < 20; i++)
        {
            var host = new TestHost();
            var gate = new Gate();
            host.Scripts.Execute = async (ctx, sink, ct) =>
            {
                if (ctx.CurrentTask is null)
                {
                    await sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
                    return;
                }
                await gate.WaitAsync(ct);
                await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
            };
            var task = (await host.SendAsync(TestHost.Text("start"))).Task!;

            using var start = new ManualResetEventSlim();
            var sends = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return host.SendAsync(TestHost.Text("continue", taskId: task.Id));
            })).ToArray();
            start.Set();

            var loser = await Task.WhenAny(sends).WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken);
            var ex = await Assert.ThrowsAsync<A2AException>(() => loser);
            Assert.Equal(A2AErrorCode.UnsupportedOperation, ex.Code);

            gate.Open();
            var winner = sends.Single(s => s != loser);
            Assert.Equal(TaskState.Completed, (await winner).Task!.Status.State);
            Assert.Equal(2, (await host.GetAsync(task.Id)).History.Count); // start + the one accepted continuation
        }
    }

    [Fact]
    public async Task Other_owner_gets_task_not_found()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
        var task = (await host.SendAsync(TestHost.Text("mine"), owner: "alice")).Task!;
        var bob = TestHost.Call("bob");

        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.GetAsync(task.Id, owner: "bob"));
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.SendAsync(TestHost.Text("hijack", taskId: task.Id), owner: "bob"));
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => host.Manager.CancelTaskAsync(bob, new CancelTaskRequest { Id = task.Id }, CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.TaskNotFound, () => TestHost.CollectAsync(host.Manager.SubscribeToTaskAsync(bob, new SubscribeToTaskRequest { Id = task.Id }, CancellationToken.None)));
        Assert.Empty((await host.Manager.ListTasksAsync(bob, new ListTasksRequest(), CancellationToken.None)).Tasks);
        Assert.Single((await host.Manager.ListTasksAsync(TestHost.Call("alice"), new ListTasksRequest(), CancellationToken.None)).Tasks);
    }

    [Fact]
    public async Task Agent_exception_fails_task_without_leaking_details()
    {
        var host = new TestHost();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            throw new InvalidOperationException("secret connection string");
        };

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
        Assert.Equal("Agent error", TextOf(task.Status.Message));
        Assert.Equal(Role.Agent, task.Status.Message!.Role);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(task, A2AJson.Options));
    }

    [Fact]
    public async Task Agent_exception_before_any_event_still_creates_failed_task()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, _, _) => throw new InvalidOperationException("boom");

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
        Assert.Equal("Agent error", TextOf(task.Status.Message));
    }

    [Fact]
    public async Task Agent_ending_without_final_state_fails_task()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.Working, null, ct);

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
        Assert.Equal("Agent ended without a final state", TextOf(task.Status.Message));
    }

    [Fact]
    public async Task Reply_after_status_update_is_invalid_agent_response_and_fails_task()
    {
        var host = new TestHost();
        var rejected = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            var ex = await Assert.ThrowsAsync<A2AException>(() => sink.ReplyAsync(TestHost.AgentText("late"), ct).AsTask());
            Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
            rejected.Set();
        };

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        await rejected.WaitAsync();
        Assert.Equal(TaskState.Failed, task.Status.State);
    }

    [Fact]
    public async Task Event_after_reply_is_invalid_agent_response()
    {
        var host = new TestHost();
        var rejected = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.ReplyAsync(TestHost.AgentText("hi"), ct);
            var ex = await Assert.ThrowsAsync<A2AException>(() => sink.UpdateStatusAsync(TaskState.Working, null, ct).AsTask());
            Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
            rejected.Set();
        };

        var response = await host.SendAsync(TestHost.Text("go"));
        await rejected.WaitAsync();

        Assert.NotNull(response.Message);
        Assert.Empty((await host.Manager.ListTasksAsync(TestHost.Call(), new ListTasksRequest(), CancellationToken.None)).Tasks);
    }

    [Fact]
    public async Task Illegal_transition_is_invalid_agent_response_and_fails_task()
    {
        var host = new TestHost();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await sink.UpdateStatusAsync(TaskState.Submitted, null, ct);
        };

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        Assert.Equal(TaskState.Failed, task.Status.State);
    }

    [Theory]
    [InlineData(TaskState.InputRequired)]
    [InlineData(TaskState.AuthRequired)]
    public async Task Illegal_agent_transition_fails_the_task(TaskState interrupted)
    {
        // D13: only the server leaves an interrupted state (a continuation or a cancel request).
        var host = new TestHost();
        var rejected = new Signal();
        A2AException? error = null;
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(interrupted, null, ct);
            error = await Assert.ThrowsAsync<A2AException>(() => sink.UpdateStatusAsync(TaskState.Completed, null, ct).AsTask());
            rejected.Set();
        };

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;
        await rejected.WaitAsync();

        Assert.Equal(interrupted, task.Status.State); // the blocking send returned at the interrupt
        Assert.Equal(A2AErrorCode.InvalidAgentResponse, error!.Code);
        Assert.Contains($"{interrupted} -> Completed", error.Message);
        var stored = await host.GetAsync(task.Id);
        Assert.Equal(TaskState.Failed, stored.Status.State);
        Assert.Equal("Invalid agent response", TextOf(stored.Status.Message));
    }

    [Fact]
    public async Task Illegal_transition_message_names_the_moves_that_are_open()
    {
        var host = new TestHost();
        var rejected = new Signal();
        A2AException? error = null;
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            error = await Assert.ThrowsAsync<A2AException>(() => sink.UpdateStatusAsync(TaskState.Submitted, null, ct).AsTask());
            rejected.Set();
        };

        await host.SendAsync(TestHost.Text("go"));
        await rejected.WaitAsync();

        Assert.StartsWith("Illegal task state transition Working -> Submitted. From Working the agent may move to: ", error!.Message);
        Assert.Contains("Completed", error.Message);
        Assert.EndsWith("See docs/spec-decisions.md.", error.Message);
    }

    [Fact]
    public async Task Late_event_from_a_paused_run_does_not_touch_the_continued_task()
    {
        var host = new TestHost();
        var firstRunSink = new TaskCompletionSource<IAgentEventSink>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRunGate = new Gate();
        host.Scripts.Execute = async (ctx, sink, ct) =>
        {
            if (ctx.CurrentTask is null)
            {
                await sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
                firstRunSink.SetResult(sink);
                return;
            }
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await secondRunGate.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };
        var task = (await host.SendAsync(TestHost.Text("start"))).Task!;
        var oldSink = await firstRunSink.Task.WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken);
        var continued = host.SendAsync(TestHost.Text("more", taskId: task.Id, returnImmediately: true));
        Assert.Equal(TaskState.Working, (await continued).Task!.Status.State);

        var ex = await Assert.ThrowsAsync<A2AException>(() => oldSink.UpdateStatusAsync(TaskState.Completed, null, CancellationToken.None).AsTask());

        Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
        Assert.Equal(TaskState.Working, (await host.GetAsync(task.Id)).Status.State);
        secondRunGate.Open();
    }

    [Fact]
    public async Task Cancel_request_moves_an_interrupted_task_to_canceled()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
        host.Scripts.Cancel = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.Canceled, null, ct);
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        var canceled = await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        Assert.Equal(TaskState.Canceled, canceled.Status.State);
        Assert.Equal(TaskState.Canceled, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Cancel_of_canceled_task_returns_task()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;
        await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        var again = await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        Assert.Equal(task.Id, again.Id);
        Assert.Equal(TaskState.Canceled, again.Status.State);
    }

    [Fact]
    public async Task Cancel_handler_setting_working_on_paused_task_still_cancels()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
        A2AException? rejected = null;
        host.Scripts.Cancel = async (_, sink, ct) =>
        {
            try { await sink.UpdateStatusAsync(TaskState.Working, null, ct); }
            catch (A2AException ex) { rejected = ex; throw; }
        };
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        var canceled = await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        Assert.Equal(A2AErrorCode.InvalidAgentResponse, rejected?.Code);
        Assert.Equal(TaskState.Canceled, canceled.Status.State);
        Assert.Equal(TaskState.Canceled, (await host.GetAsync(task.Id)).Status.State);
    }

    [Theory]
    [InlineData(TaskState.InputRequired, "completed")]
    [InlineData(TaskState.AuthRequired, "working")]
    [InlineData(TaskState.AuthRequired, "reply")]
    [InlineData(TaskState.InputRequired, "bad-artifact")]
    public async Task Illegal_cancel_handler_event_on_paused_task_still_cancels(TaskState paused, string illegalEvent)
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(paused, null, ct);
        A2AException? rejected = null;
        host.Scripts.Cancel = async (_, sink, ct) =>
        {
            try
            {
                switch (illegalEvent)
                {
                    case "completed": await sink.UpdateStatusAsync(TaskState.Completed, null, ct); break;
                    case "working": await sink.UpdateStatusAsync(TaskState.Working, null, ct); break;
                    case "reply": await sink.ReplyAsync(TestHost.AgentText("hi"), ct); break;
                    default: await sink.AddArtifactAsync(new Artifact(), append: false, lastChunk: true, ct); break;
                }
            }
            catch (A2AException ex) { rejected = ex; }
        };
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        var canceled = await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        Assert.Equal(A2AErrorCode.InvalidAgentResponse, rejected?.Code);
        Assert.Equal(TaskState.Canceled, canceled.Status.State);
        Assert.Equal(TaskState.Canceled, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Cancel_handler_event_after_it_paused_the_task_still_cancels()
    {
        var host = new TestHost();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await Task.Delay(Timeout.Infinite, ct);
        };
        A2AException? rejected = null;
        host.Scripts.Cancel = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.InputRequired, null, ct);
            try { await sink.UpdateStatusAsync(TaskState.Working, null, ct); }
            catch (A2AException ex) { rejected = ex; }
        };
        var task = (await host.SendAsync(TestHost.Text("go", returnImmediately: true))).Task!;

        var canceled = await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        Assert.Equal(A2AErrorCode.InvalidAgentResponse, rejected?.Code);
        Assert.Equal(TaskState.Canceled, canceled.Status.State);
        Assert.Equal(TaskState.Canceled, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Event_after_terminal_state_is_rejected_and_task_unchanged()
    {
        var host = new TestHost();
        var rejected = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
            var ex = await Assert.ThrowsAsync<A2AException>(() => sink.UpdateStatusAsync(TaskState.Working, null, ct).AsTask());
            Assert.Equal(A2AErrorCode.InvalidAgentResponse, ex.Code);
            rejected.Set();
        };

        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;
        await rejected.WaitAsync();

        Assert.Equal(TaskState.Completed, task.Status.State);
        Assert.Equal(TaskState.Completed, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Cancel_running_task_returns_canceled()
    {
        var host = new TestHost();
        var agentCancelled = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { agentCancelled.Set(); throw; }
        };
        var task = (await host.SendAsync(TestHost.Text("go", returnImmediately: true))).Task!;
        Assert.Equal(TaskState.Working, task.Status.State);

        var canceled = await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);

        Assert.Equal(TaskState.Canceled, canceled.Status.State);
        await agentCancelled.WaitAsync();
        Assert.Equal(TaskState.Canceled, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Cancel_releases_a_blocking_send()
    {
        var host = new TestHost();
        var entered = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            entered.Set();
            await Task.Delay(Timeout.Infinite, ct);
        };
        var send = host.SendAsync(TestHost.Text("go"));
        await entered.WaitAsync();
        var id = (await host.Manager.ListTasksAsync(TestHost.Call(), new ListTasksRequest(), CancellationToken.None)).Tasks.Single().Id;

        await host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = id }, CancellationToken.None);

        Assert.Equal(TaskState.Canceled, (await send).Task!.Status.State);
    }

    [Fact]
    public async Task Continuation_during_cancel_is_rejected_and_does_not_hang()
    {
        var host = new TestHost();
        var cancelEntered = new Signal();
        var cancelGate = new Gate();
        var continuationRan = new Signal();
        host.Scripts.Execute = async (ctx, sink, ct) =>
        {
            if (ctx.CurrentTask is null) { await sink.UpdateStatusAsync(TaskState.InputRequired, null, ct); return; }
            continuationRan.Set();
            await Task.Delay(Timeout.Infinite, ct);
        };
        host.Scripts.Cancel = async (_, sink, ct) =>
        {
            cancelEntered.Set();
            await cancelGate.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Canceled, null, ct);
        };
        var task = (await host.SendAsync(TestHost.Text("start"))).Task!;

        var cancel = host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None);
        await cancelEntered.WaitAsync();
        await ThrowsA2A(A2AErrorCode.UnsupportedOperation, () => host.SendAsync(TestHost.Text("more", taskId: task.Id)));
        cancelGate.Open();

        Assert.Equal(TaskState.Canceled, (await cancel.WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken)).Status.State);
        Assert.Equal(TaskState.Canceled, (await host.GetAsync(task.Id)).Status.State);
        Assert.Single(host.Scripts.Contexts); // the continuation's agent never started
    }

    [Fact]
    public async Task Blocking_send_returns_the_final_state_even_if_the_task_moves_on()
    {
        var host = new TestHost();
        host.Scripts.Execute = (ctx, sink, ct) => ctx.CurrentTask is null
            ? sink.UpdateStatusAsync(TaskState.InputRequired, TestHost.AgentText("more?"), ct)
            : sink.UpdateStatusAsync(TaskState.Completed, null, ct);

        for (var i = 0; i < 20; i++)
        {
            var first = (await host.SendAsync(TestHost.Text("start"))).Task!;
            Assert.Equal(TaskState.InputRequired, first.Status.State);
            Assert.Equal(TaskState.Completed, (await host.SendAsync(TestHost.Text("go", taskId: first.Id))).Task!.Status.State);
        }
    }

    [Fact]
    public async Task Cancel_completed_task_throws_not_cancelable()
    {
        var host = new TestHost();
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        await ThrowsA2A(A2AErrorCode.TaskNotCancelable,
            () => host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = task.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_unknown_task_throws_task_not_found()
    {
        var host = new TestHost();

        await ThrowsA2A(A2AErrorCode.TaskNotFound,
            () => host.Manager.CancelTaskAsync(TestHost.Call(), new CancelTaskRequest { Id = "nope" }, CancellationToken.None));
    }

    [Fact]
    public async Task Stream_yields_task_then_updates_then_closes_on_terminal()
    {
        var host = new TestHost();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await sink.AddArtifactAsync(new Artifact { ArtifactId = "a1", Parts = [Part.FromText("x")] }, false, true, ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };

        var events = await TestHost.CollectAsync(
            host.Manager.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("go"), CancellationToken.None));

        Assert.Equal(4, events.Count);
        var task = events[0].Task!;
        Assert.Equal(TaskState.Submitted, task.Status.State);
        Assert.Single(task.History);
        Assert.Equal(TaskState.Working, events[1].StatusUpdate!.Status.State);
        Assert.Equal("a1", events[2].ArtifactUpdate!.Artifact.ArtifactId);
        Assert.True(events[2].ArtifactUpdate!.LastChunk);
        Assert.Equal(TaskState.Completed, events[3].StatusUpdate!.Status.State);
        Assert.All(events.Skip(1), e =>
        {
            Assert.Equal(task.Id, e.StatusUpdate?.TaskId ?? e.ArtifactUpdate!.TaskId);
            Assert.Equal(task.ContextId, e.StatusUpdate?.ContextId ?? e.ArtifactUpdate!.ContextId);
        });
    }

    [Fact]
    public async Task Stream_with_direct_reply_yields_single_message()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.ReplyAsync(TestHost.AgentText("hi"), ct);

        var events = await TestHost.CollectAsync(
            host.Manager.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("go"), CancellationToken.None));

        Assert.Equal("hi", TextOf(Assert.Single(events).Message));
    }

    [Fact]
    public async Task Stream_closes_on_input_required_and_continuation_streams_again()
    {
        var host = new TestHost();
        host.Scripts.Execute = (ctx, sink, ct) => ctx.CurrentTask is null
            ? sink.UpdateStatusAsync(TaskState.InputRequired, null, ct)
            : sink.UpdateStatusAsync(TaskState.Completed, null, ct);

        var first = await TestHost.CollectAsync(host.Manager.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("a"), CancellationToken.None));
        var id = first[0].Task!.Id;
        var second = await TestHost.CollectAsync(host.Manager.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("b", taskId: id), CancellationToken.None));

        Assert.Equal(TaskState.InputRequired, first[^1].StatusUpdate!.Status.State);
        Assert.Equal(TaskState.Submitted, second[0].Task!.Status.State);
        Assert.Equal(2, second[0].Task!.History.Count);
        Assert.Equal(TaskState.Completed, second[^1].StatusUpdate!.Status.State);
    }

    [Fact]
    public async Task Stream_is_unsupported_when_card_does_not_declare_streaming()
    {
        var host = new TestHost(TestHost.DefaultCard with { Capabilities = new AgentCapabilities { Streaming = false } });

        await ThrowsA2A(A2AErrorCode.UnsupportedOperation,
            () => TestHost.CollectAsync(host.Manager.SendStreamingMessageAsync(TestHost.Call(), TestHost.Text("go"), CancellationToken.None)));
    }

    [Fact]
    public async Task Subscribe_to_terminal_task_is_unsupported()
    {
        var host = new TestHost();
        var task = (await host.SendAsync(TestHost.Text("go"))).Task!;

        await ThrowsA2A(A2AErrorCode.UnsupportedOperation,
            () => TestHost.CollectAsync(host.Manager.SubscribeToTaskAsync(TestHost.Call(), new SubscribeToTaskRequest { Id = task.Id }, CancellationToken.None)));
    }

    [Fact]
    public async Task Subscribe_yields_current_task_then_live_events()
    {
        var host = new TestHost();
        var gate = new Gate();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await gate.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
        };
        var task = (await host.SendAsync(TestHost.Text("go", returnImmediately: true))).Task!;

        var stream = host.Manager.SubscribeToTaskAsync(TestHost.Call(), new SubscribeToTaskRequest { Id = task.Id }, CancellationToken.None);
        await using var e = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await e.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(TaskState.Working, e.Current.Task!.Status.State);
        gate.Open();
        Assert.True(await e.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(TaskState.Completed, e.Current.StatusUpdate!.Status.State);
        Assert.False(await e.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Subscriber_cancellation_does_not_cancel_task()
    {
        var host = new TestHost();
        var gate = new Gate();
        var done = new Signal();
        host.Scripts.Execute = async (_, sink, ct) =>
        {
            await sink.UpdateStatusAsync(TaskState.Working, null, ct);
            await gate.WaitAsync(ct);
            await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
            done.Set();
        };
        var task = (await host.SendAsync(TestHost.Text("go", returnImmediately: true))).Task!;
        var request = new SubscribeToTaskRequest { Id = task.Id };

        using var cts = new CancellationTokenSource();
        await using var cancelled = host.Manager.SubscribeToTaskAsync(TestHost.Call(), request, cts.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using var kept = host.Manager.SubscribeToTaskAsync(TestHost.Call(), request, CancellationToken.None).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await cancelled.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
        Assert.True(await kept.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(2, host.Broker.SubscriberCount(TestHost.AgentName, task.Id));

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(1, host.Broker.SubscriberCount(TestHost.AgentName, task.Id));

        gate.Open();
        await done.WaitAsync();
        Assert.True(await kept.MoveNextAsync().AsTask().WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(TaskState.Completed, kept.Current.StatusUpdate!.Status.State);
        Assert.Equal(TaskState.Completed, (await host.GetAsync(task.Id)).Status.State);
    }

    [Fact]
    public async Task Invalid_parts_throw_invalid_params()
    {
        var host = new TestHost();
        var request = TestHost.Text("x");

        await ThrowsA2A(A2AErrorCode.InvalidParams, () => host.SendAsync(request with { Message = request.Message with { Parts = [] } }));
        await ThrowsA2A(A2AErrorCode.InvalidParams, () => host.SendAsync(request with { Message = request.Message with { Parts = [new Part()] } }));
        await ThrowsA2A(A2AErrorCode.InvalidParams, () => host.SendAsync(request with { Message = request.Message with { MessageId = "" } }));
    }

    [Fact]
    public async Task Unsupported_input_media_type_throws_content_type_not_supported()
    {
        var host = new TestHost();
        var request = TestHost.Text("x");
        request = request with { Message = request.Message with { Parts = [Part.FromRaw([1, 2, 3], "image/png")] } };

        await ThrowsA2A(A2AErrorCode.ContentTypeNotSupported, () => host.SendAsync(request));
    }

    [Fact]
    public async Task Wildcard_input_mode_accepts_matching_media_type()
    {
        var host = new TestHost(TestHost.DefaultCard with { DefaultInputModes = ["text/plain", "image/*"] });
        var request = TestHost.Text("x");
        request = request with { Message = request.Message with { Parts = [Part.FromRaw([1], "IMAGE/PNG; q=1")] } };

        Assert.Equal(TaskState.Completed, (await host.SendAsync(request)).Task!.Status.State);
    }

    [Fact]
    public async Task Missing_required_extension_throws_extension_support_required()
    {
        var card = TestHost.DefaultCard with
        {
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
                Extensions = [new AgentExtension { Uri = "urn:ext:required", Required = true }, new AgentExtension { Uri = "urn:ext:optional" }],
            },
        };
        var host = new TestHost(card);

        await ThrowsA2A(A2AErrorCode.ExtensionSupportRequired,
            () => host.Manager.SendMessageAsync(TestHost.Call("alice", "urn:ext:optional"), TestHost.Text("x"), CancellationToken.None));

        await host.Manager.SendMessageAsync(TestHost.Call("alice", "urn:ext:required", "urn:ext:unknown"), TestHost.Text("x"), CancellationToken.None);
        Assert.Equal(["urn:ext:required"], host.Scripts.Contexts.Single().ActiveExtensions);
    }

    [Fact]
    public async Task Extended_card_not_configured_throws()
    {
        var extended = TestHost.DefaultCard with { Description = "extended" };
        var withCapability = TestHost.DefaultCard with { Capabilities = new AgentCapabilities { ExtendedAgentCard = true } };

        await ThrowsA2A(A2AErrorCode.UnsupportedOperation,
            () => new TestHost(extendedCard: extended).Manager.GetExtendedAgentCardAsync(TestHost.Call(), new GetExtendedAgentCardRequest(), CancellationToken.None));
        await ThrowsA2A(A2AErrorCode.ExtendedAgentCardNotConfigured,
            () => new TestHost(withCapability).Manager.GetExtendedAgentCardAsync(TestHost.Call(), new GetExtendedAgentCardRequest(), CancellationToken.None));

        var card = await new TestHost(withCapability, extended).Manager.GetExtendedAgentCardAsync(TestHost.Call(), new GetExtendedAgentCardRequest(), CancellationToken.None);
        Assert.Equal("extended", card.Description);
    }

    [Fact]
    public async Task History_length_zero_returns_no_history()
    {
        var host = new TestHost();
        host.Scripts.Execute = (_, sink, ct) => sink.UpdateStatusAsync(TaskState.Completed, TestHost.AgentText("done"), ct);

        var task = (await host.SendAsync(TestHost.Text("go", historyLength: 0))).Task!;

        Assert.Empty(task.History);
        Assert.Empty((await host.GetAsync(task.Id, historyLength: 0)).History);
        Assert.Equal(["done"], (await host.GetAsync(task.Id, historyLength: 1)).History.Select(TextOf));
        Assert.Equal(2, (await host.GetAsync(task.Id)).History.Count);
        await ThrowsA2A(A2AErrorCode.InvalidParams, () => host.GetAsync(task.Id, historyLength: -1));
    }

    [Fact]
    public async Task Observers_see_every_task_event_in_order()
    {
        var observer = new RecordingObserver();
        var scripts = new AgentScripts
        {
            Execute = async (_, sink, ct) =>
            {
                await sink.UpdateStatusAsync(TaskState.Working, null, ct);
                await sink.UpdateStatusAsync(TaskState.Completed, null, ct);
            },
        };
        var provider = new ServiceCollection().AddSingleton(scripts).BuildServiceProvider();
        var manager = new TaskManager(
            new AgentCatalog([new AgentRegistration(TestHost.AgentName, typeof(ScriptedAgent), TestHost.DefaultCard, null, null)]),
            new InMemoryTaskStore(), new TaskEventBroker(), provider, [observer]);

        await manager.SendMessageAsync(TestHost.Call(), TestHost.Text("go"), CancellationToken.None).WaitAsync(TestHost.Timeout, TestContext.Current.CancellationToken);

        Assert.Collection(observer.Events,
            e => Assert.Equal(TaskState.Submitted, e.Task!.Status.State),
            e => Assert.Equal(TaskState.Working, e.StatusUpdate!.Status.State),
            e => Assert.Equal(TaskState.Completed, e.StatusUpdate!.Status.State));
    }

    private sealed class RecordingObserver : ITaskEventObserver
    {
        public List<StreamResponse> Events { get; } = [];

        public ValueTask OnTaskEventAsync(StoredTask task, StreamResponse evt, CancellationToken ct)
        {
            lock (Events) Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
