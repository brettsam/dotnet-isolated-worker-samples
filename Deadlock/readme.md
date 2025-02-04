This project contains a fairly common deadlocking pattern that can be difficult to diagnose in a Function app. It will mostly only reproduce under load during initialization. This means that a slow ramp-up of an instance runs fine, while something like a direct deployment to production (with traffic) can cause a deadlock.

This sample will focus on how to gather a dump from production and analyze it for similar deadlock patterns.

## Reproduce
It's easiest to reproduce the deadlock in this sample by deploying it to a non-consumption Function App (to prevent scale out) or limiting the instances to some low number, ideally 1. Once deployed, you should see that a single request to the `/api/function1` endpoint returns successfully.

Once deployed, you need to generate some load. For example, you can use [bombardier](https://github.com/codesenberg/bombardier) to simulate load with something like this to simulate 500 concurrent requests for 1 minute:

```
bombardier https://{yoursite}.azurewebsites.net/api/function1 -c 500 -d 1m -t 15s
```
When that load is running, restart the application via the Azure portal. This will cause all instances to stop and restart. This *should* result in the deadlock case, but you may need to try several times as it still is a race condition.

## Capture the dump
This can vary by platform:
### Windows
1. Navigate to Kudu for your site and go to `Debug Console` -> `PowerShell`. While the `CMD` prompt also works, in my experience, the `PowerShell` prompt is more responsive while under load.

2. Install the `dotnet-dump` tool with:
   ```
   dotnet tool install --global dotnet-dump
   ```
3. Add `dotnet-dump` to your `PATH` with:
   ```ps
   $env:PATH += ";C:\local\UserProfile\.dotnet\tools"
   ```
4. In a separate tab, open up another Kudu window and go to `Process explorer`. Find the process id of the `dotnet.exe` that corresponds to your worker. It'll be a child of the non-scm `w3wp.exe`

5. Capture a dump with:
   ```
   dotnet-dump collect -p {process id}
   ```
6. You should now have a *.dmp file in the home directory of your site. You can download this directly from the browser to your local machine for analysis.
### Linux
*coming soon*

## Analyze the dump
When approaching dump analysis, it's a good idea to have some kind of hypothesis about what you're looking for. In this case, logs can show us certain things that lead us toward some kind of deadlock... likely during initialization:
- when under light load, things run fine
- when under heavier load and the instance restarts, we begin getting timeouts from requests going to the service
- some metrics (may) be showing thread usage creeping up

If we expect a deadlock here, the goal is to find out where it's happening. To do this, you can use several tools:
- open in Visual Studio -- this is typically my first choice but I've run into issues when analyzing Linux dumps
- use [WinDbg](https://aka.ms/WinDbg)
- use [dotnet-dump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump) -- we'll focus on this

The tool `dotnet-dump` has a lot of the same commands as `WinDbg`, but it's much easier to install and use for dotnet debugging. To install, run the same command as you did on the instance above:

```
dotnet tool install --global dotnet-dump
```

Then, you can start analyzing the dump with
```
dotnet-dump analyze ./dump_abc123.dmp
```

This will enter you into an interactive shell where you can run various debugging commands.

In this scenario, I want to see a summary of threads being used by the CLR to see if anything jumps out. To do this, I'll run `pstacks`, which will show me a view similar to that of the `Parallel Stacks` view in Visual Studio.

When I run this, one of the last things it writes is:

```
==> 346 threads with 2 roots
```

346 threads on an app that had run less than a minute seems like a lot. My guess is the deadlock was causing the runtime to allocate more threads as it could not find one to run on (they were all locked).

Looking through the merged stacks (not all is pasted here; it can be quite large), I start to see large clumps of threads stuck at similar points:

```
               ...
               ~~~~ 4f2c,1e8,174c,2cfc...
                 252 Deadlock.SomeFactory.GetData()
                 252 Deadlock.DirectFunctionExecutor+<ExecuteAsync>d__3.MoveNext()
                 252 System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start(<ExecuteAsync>d__3 ByRef)
               ...
```

```
                               ...
                               ~~~~ 2ab8,5f4,4ab4,3510...
                                 25 System.Threading.Monitor.Enter(Object, Boolean ByRef)
                                 25 Deadlock.SomeFactory.GetData()
                                 25 Deadlock.Function1.Run(HttpRequest)
                               ...
```

```
                     ...
                     ~~~~ 4c2c,1b74,2bc4,4a9c...
                       11 System.Threading.Monitor.Enter(Object, Boolean ByRef)
                       11 Deadlock.SomeFactory.GetData()
                       11 Deadlock.Function1.Run(HttpRequest)
                     ...
```
Lots of calls to `GetData()` seem to be stuck. Then you can see one stack that only one thread is using:

```
                                   ...
                                   ~~~~ 4b90
                                       1 System.Threading.ManualResetEventSlim.Wait(Int32, CancellationToken)
                                       1 System.Threading.Tasks.Task.SpinThenBlockingWait(Int32, CancellationToken)
                                       1 System.Threading.Tasks.Task.InternalWaitCore(Int32, CancellationToken)
                                       1 System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task, ConfigureAwaitOptions)
                                       1 System.Runtime.CompilerServices.TaskAwaiter<System.__Canon>.GetResult()
                                       ... 
                                       1 System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.__Canon>.AsyncTaskMethodBuilder`1(<CreateIfNotExistsInternal>d__51 ByRef)
                                       1 Azure.Storage.Blobs.BlobContainerClient.CreateIfNotExistsInternal(PublicAccessType, IDictionary<String,String>, BlobContainerEncryptionScopeOptions, Boolean, CancellationToken)
                                       1 Azure.Storage.Blobs.BlobContainerClient.CreateIfNotExists(PublicAccessType, IDictionary<String,String>, BlobContainerEncryptionScopeOptions, CancellationToken)
                                       1 Deadlock.SomeFactory.RetrieveDataFromExternalService()
                                  ...
```
This appears to be a thread calling `RetrieveDataFromExternalService()` that, internally, is using a `GetAwaiter().GetResult()`. This is a very common [deadlock pattern](https://blog.stephencleary.com/2012/07/dont-block-on-async-code.html).

You can also try running `syncblk` for more proof:
```
> syncblk
Index SyncBlock MonitorHeld Recursion Owning Thread Info  SyncBlock Owner
    7 09E2A714          681         1 09E36238 4b90   8   046b3a28 System.Object
```
This tells us that there are [340 threads (340 x 2 + 1 = 681)](https://stackoverflow.com/a/2203085) waiting on this one lock that is owned by thread 8. If we look at thread 8, that, indeed is the stack that we saw above.

For another view of that thread's stack, we can use `threads 8` to set the current thread and then see its stack with `clrstack`:
```
> threads 8
> clrstack
OS Thread Id: 0x4b90 (8)
Child SP       IP Call Site
0AA5D128 7789443c [HelperMethodFrame_1OBJ: 0aa5d128] System.Threading.Monitor.ObjWait(Int32, System.Object)
0AA5D1A4 12F455CA System.Threading.ManualResetEventSlim.Wait(Int32, System.Threading.CancellationToken) [/_/src/libraries/System.Private.CoreLib/src/System/Threading/ManualResetEventSlim.cs @ 561]
0AA5D224 6CA55D51 System.Threading.Tasks.Task.SpinThenBlockingWait(Int32, System.Threading.CancellationToken) [/_/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs @ 3072]
0AA5D268 6CA55B89 System.Threading.Tasks.Task.InternalWaitCore(Int32, System.Threading.CancellationToken) [/_/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs @ 3007]
0AA5D294 6CAA0891 System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(System.Threading.Tasks.Task, System.Threading.Tasks.ConfigureAwaitOptions) [/_/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs @ 104]
...
```
Now, how to fix the bug? Well, as recommended in the link above, use `async` everywhere. Looking at the `SomeFactory.GetData()` call that we see everywhere, we can see:

```csharp
public IDictionary<string, string> GetData()
{
    if (_data == null)
    {
        lock (_lock)
        {
            _data ??= RetrieveDataFromExternalService();
        }
    }

    return _data;
}
```

And `RetrieveDataFromExternalService()` uses several synchronous calls to retrieve data from Storage Blobs. 

```csharp
private static IDictionary<string, string> RetrieveDataFromExternalService()
{
    var connStr = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

    BlobServiceClient service = new(connStr);
    BlobContainerClient container = service.GetBlobContainerClient("deadlock");
    container.CreateIfNotExists(); // <-- this is the deadlock in the stack above
    ...
```
Those calls, [internally, are using `GetAwaiter().GetResult()`](https://github.com/Azure/azure-sdk-for-net/blob/13e31b39bed3e87ea4b5db19440aa144117c0fd6/sdk/core/System.ClientModel/src/Internal/TaskExtensions.cs#L30), leading to the deadlock. The fix here is to transition all of these call to their `async` counterparts. This may require a decent amount of refactoring, but it will fix the bug and lead to a more responsive, reliable service.

## Other links:
- https://gabrielweyer.net/2018/05/05/windbg-2-blocked-async/