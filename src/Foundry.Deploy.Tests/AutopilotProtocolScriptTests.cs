// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotProtocolScriptTests
{
    [Theory]
    [InlineData("auth-timeout", "TimedOut")]
    [InlineData("auth-error", "AuthenticationFailed")]
    [InlineData("capture-timeout", "TimedOut")]
    [InlineData("capture-error", "CaptureUnavailable")]
    [InlineData("cancel", null)]
    public async Task Worker_PreservesSafeFailureCodesAndDoesNotOverwriteOnClose(string scenario, string? code)
    {
        await RunAsync("""
            $state=@{result=$null}
            $cts=[Threading.CancellationTokenSource]::new()
            $events=[Collections.Concurrent.ConcurrentQueue[string]]::new()
            $commands=[Collections.Concurrent.BlockingCollection[string]]::new(1)
            $commands.Add('{"kind":"upload","groupTag":"fixture"}')
            function Get-FoundryAutopilotToken {
                param($context)
                switch($env:FOUNDRY_SCENARIO){
                    auth-timeout {throw 'TimedOut'}
                    auth-error {throw 'private-sensitive-error'}
                    cancel {$cts.Cancel();$context.Token.ThrowIfCancellationRequested()}
                }
                return 'private-token'
            }
            function Get-FoundryAutopilotGroupTags {param($context);return @()}
            function Get-FoundryAutopilotHardwareIdentity {
                param($context,$deadline)
                if($env:FOUNDRY_SCENARIO -eq 'capture-timeout'){throw 'TimedOut'}
                throw 'private-sensitive-provider-error'
            }
            function Write-FoundryAutopilotOutcome {param($config,$result);$state.result=$result;$cts.Cancel()}
            Start-FoundryAutopilotWorker '{}' $events $commands $cts.Token
            if($env:FOUNDRY_SCENARIO -eq 'cancel') {if($null -ne $state.result){throw 'Close overwrote terminal result.'}}
            elseif($state.result.code -ne $env:FOUNDRY_EXPECTED){throw 'Failure cause lost.'}
            $json=$null
            while($events.TryDequeue([ref]$json)){if($json.Contains('private-')){throw 'Sensitive error crossed queue.'}}
            $commands.Dispose();$cts.Dispose()
            """, new() { ["FOUNDRY_SCENARIO"] = scenario, ["FOUNDRY_EXPECTED"] = code });
    }

    [Theory]
    [InlineData("invalid-hash")]
    [InlineData("next-link")]
    [InlineData("pending-timeout")]
    [InlineData("visibility-timeout")]
    public async Task Import_RejectsIncompleteEvidenceAndExhaustedBudgets(string scenario)
    {
        await RunAsync("""
            $fixture=Get-Content -LiteralPath $env:FOUNDRY_FIXTURE -Raw | ConvertFrom-Json
            $state=@{now=[DateTimeOffset]::UtcNow;calls=0;updates=0}
            if($env:FOUNDRY_SCENARIO -eq 'invalid-hash'){$fixture.request.hardwareIdentifier='invalid'}
            if($env:FOUNDRY_SCENARIO -eq 'next-link'){$fixture.importResponse|Add-Member NoteProperty '@odata.nextLink' 'https://graph.microsoft.com/v1.0/more'}
            if($env:FOUNDRY_SCENARIO -eq 'pending-timeout'){$fixture.importResponse.value[0].state.deviceImportStatus='pending'}
            $context=@{Config=@{graphBaseUri='https://graph.microsoft.com/v1.0'};Token=[Threading.CancellationToken]::None;
                Now={$state.now};Sleep={param($seconds,$token)$state.now=$state.now.AddSeconds($seconds)};
                Transport={param($context,$method,$uri,$body,$deadline)
                    $state.calls++
                    if($uri.EndsWith('/import')){return @{statusCode=200;body=$fixture.importResponse}}
                    if($uri.Contains('/importedWindowsAutopilotDeviceIdentities/')){return @{statusCode=200;body=$fixture.importResponse.value[0]}}
                    if($uri.EndsWith('/updateDeviceProperties')){$state.updates++;throw 'Unexpected mutation.'}
                    return @{statusCode=404}
                }}
            $started=$state.now
            $result=Invoke-FoundryAutopilotImport $context $fixture.request
            if($result.status -ne 'failed' -or $state.updates -ne 0){throw 'Unverified import accepted.'}
            switch($env:FOUNDRY_SCENARIO){
                invalid-hash {if($result.code -ne 'IdentityUnconfirmed' -or $state.calls -ne 0){throw 'Invalid hash sent.'}}
                next-link {if($result.code -ne 'IdentityUnconfirmed' -or $state.calls -ne 1){throw 'Partial POST accepted.'}}
                pending-timeout {if($result.code -ne 'TimedOut' -or ($state.now-$started).TotalSeconds -ne 900){throw 'Workflow budget incorrect.'}}
                visibility-timeout {if($result.code -ne 'TimedOut' -or ($state.now-$started).TotalSeconds -ne 600){throw 'Visibility budget incorrect.'}}
            }
            """, new()
        {
            ["FOUNDRY_SCENARIO"] = scenario,
            ["FOUNDRY_FIXTURE"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Autopilot", "complete.json")
        });
    }

    [Theory]
    [InlineData("headers")]
    [InlineData("body")]
    [InlineData("cancel")]
    [InlineData("throttled-html")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    [InlineData("tls")]
    public async Task HttpTransport_BoundsBodiesAndPreservesStatusClassification(string scenario)
    {
        await RunAsync("""
            Add-Type -AssemblyName System.Net.Http
            Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @'
            using System; using System.IO; using System.Net; using System.Net.Http; using System.Threading; using System.Threading.Tasks;
            public sealed class StalledHandler : HttpMessageHandler {
                public static string Mode; public static bool Disposed; public static bool StreamDisposed; public static int Calls;
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
                    Calls++;
                    if(Mode=="tls") throw new HttpRequestException("private TLS error",new System.Security.Authentication.AuthenticationException());
                    if(Mode=="headers" || Mode=="cancel") { var t=new TaskCompletionSource<HttpResponseMessage>();token.Register(()=>t.TrySetCanceled());return t.Task; }
                    if(Mode=="throttled-html") { var r=new HttpResponseMessage((HttpStatusCode)429){Content=new StringContent("<html>throttled</html>")};r.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));return Task.FromResult(r); }
                    if(Mode=="malformed") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("not-json")});
                    if(Mode=="oversized") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',2097153))});
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StalledStream())});
                }
                protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
            }
            public sealed class StalledStream : Stream {
                public override bool CanRead{get{return true;}} public override bool CanWrite{get{return false;}} public override bool CanSeek{get{return false;}}
                public override long Length{get{throw new NotSupportedException();}} public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
                public override Task<int> ReadAsync(byte[] b,int o,int c,CancellationToken token){var t=new TaskCompletionSource<int>();token.Register(()=>t.TrySetCanceled());return t.Task;}
                public override int Read(byte[] b,int o,int c){throw new NotSupportedException();}
                public override void Flush(){} public override void SetLength(long l){throw new NotSupportedException();}
                public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();} public override void Write(byte[]b,int o,int c){throw new NotSupportedException();}
                protected override void Dispose(bool disposing){StalledHandler.StreamDisposed=true;base.Dispose(disposing);}
            }
            '@
            [StalledHandler]::Mode=$env:FOUNDRY_SCENARIO
            $cts=[Threading.CancellationTokenSource]::new()
            $context=@{Token=$cts.Token;Now={[DateTimeOffset]::UtcNow};AccessToken='fixture';HttpClientFactory={ [Net.Http.HttpClient]::new([StalledHandler]::new()) }}
            if($env:FOUNDRY_SCENARIO -eq 'cancel') {$cts.CancelAfter(200)}
            $failed=$false
            $milliseconds=if($env:FOUNDRY_SCENARIO -in @('headers','body','cancel')){400}else{5000}
            try {$response=Invoke-FoundryHttp $context GET 'https://graph.microsoft.com/v1.0/fixture' $null ([DateTimeOffset]::UtcNow.AddMilliseconds($milliseconds))}
            catch {
                $expected= switch($env:FOUNDRY_SCENARIO){malformed {'InvalidResponse'};oversized {'ResponseTooLarge'};tls {'SecureChannelFailed'};default {'TimedOut'}}
                $failed=($env:FOUNDRY_SCENARIO -eq 'cancel' -and $cts.IsCancellationRequested) -or $_.Exception.Message -eq $expected
            }
            if($env:FOUNDRY_SCENARIO -eq 'throttled-html'){$failed=$response.statusCode -eq 429 -and $response.retryAfter -eq 12}
            if(-not $failed -or [StalledHandler]::Calls -ne 1 -or -not [StalledHandler]::Disposed){throw 'Transport classification/disposal incorrect.'}
            if($env:FOUNDRY_SCENARIO -eq 'body' -and -not [StalledHandler]::StreamDisposed){throw 'Response body retained.'}
            $cts.Dispose()
            """, new() { ["FOUNDRY_SCENARIO"] = scenario });
    }

    [Theory]
    [InlineData("slow-down")]
    [InlineData("expiry")]
    [InlineData("server-expiry")]
    [InlineData("terminal")]
    public async Task Authentication_HonorsServerIntervalExpiryAndTerminalErrors(string scenario)
    {
        await RunAsync("""
            $state=@{now=[DateTimeOffset]::UtcNow;calls=0;delays=[Collections.Generic.List[double]]::new()}
            $context=@{Token=[Threading.CancellationToken]::None;Config=@{tenant='common';clientId='fixture';scopes=@('scope')};Events=[Collections.Concurrent.ConcurrentQueue[string]]::new();
                Now={$state.now};Sleep={param($seconds,$token) $state.delays.Add($seconds);$state.now=$state.now.AddSeconds($seconds)};
                Transport={param($context,$method,$uri,$body,$deadline)
                    if($uri.EndsWith('/devicecode')) {return @{statusCode=200;body=@{device_code='private-code';user_code='display-code';expires_in=20;interval=5}}}
                    $state.calls++
                    if($env:FOUNDRY_SCENARIO -eq 'terminal'){return @{statusCode=400;body=@{error='access_denied'}}}
                    if($env:FOUNDRY_SCENARIO -eq 'server-expiry'){return @{statusCode=400;body=@{error='expired_token'}}}
                    if($env:FOUNDRY_SCENARIO -eq 'expiry'){return @{statusCode=400;body=@{error='authorization_pending'}}}
                    if($state.calls -eq 1){return @{statusCode=400;body=@{error='slow_down'}}}
                    return @{statusCode=200;body=@{access_token='private-token'}}
                }}
            $failure=$null;$token=$null
            try {$token=Get-FoundryAutopilotToken $context} catch {$failure=$_.Exception.Message}
            switch($env:FOUNDRY_SCENARIO) {
                slow-down {if($token -ne 'private-token' -or ($state.delays -join ',') -ne '5,10'){throw 'Slow-down was ignored.'}}
                expiry {if($failure -ne 'TimedOut' -or $state.calls -ne 3){throw 'Expired device code polled.'}}
                server-expiry {if($failure -ne 'TimedOut' -or $state.calls -ne 1){throw 'Server expiry cause lost.'}}
                terminal {if($failure -ne 'AuthenticationFailed' -or $state.calls -ne 1){throw 'Terminal OAuth error retried.'}}
            }
            $json=$null
            while($context.Events.TryDequeue([ref]$json)){if($json.Contains('private-')){throw 'Secret crossed UI queue.'}}
            """, new() { ["FOUNDRY_SCENARIO"] = scenario });
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("foreign")]
    public async Task GroupDiscovery_RejectsUntrustedOrCyclicPagination(string scenario)
    {
        await RunAsync("""
            $state=@{now=[DateTimeOffset]::UtcNow;calls=0}
            $context=@{Token=[Threading.CancellationToken]::None;Config=@{graphBaseUri='https://graph.microsoft.com/v1.0'};Now={$state.now};
                Transport={param($context,$method,$uri,$body,$deadline)
                    $state.calls++
                    $next=if($env:FOUNDRY_SCENARIO -eq 'cycle'){$uri}else{'https://untrusted.invalid/steal'}
                    return @{statusCode=200;body=@{value=@();'@odata.nextLink'=$next}}
                }}
            $failure=$null
            try{$null=Get-FoundryAutopilotGroupTags $context}catch{$failure=$_.Exception.Message}
            if(-not $failure -or $state.calls -ne 1){throw 'Unsafe pagination was followed.'}
            """, new() { ["FOUNDRY_SCENARIO"] = scenario });
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("cancel")]
    [InlineData("retry-after")]
    [InlineData("foreign")]
    [InlineData("terminal")]
    [InlineData("mutation503")]
    [InlineData("invalid-response")]
    public async Task Requests_RespectDeadlineCancellationAndRetryPolicy(string scenario)
    {
        await RunAsync("""
            $state=@{now=[DateTimeOffset]::UtcNow;calls=0;delay=0}
            $cts=[Threading.CancellationTokenSource]::new()
            $context=@{Config=@{graphBaseUri='https://graph.microsoft.com/v1.0'};Token=$cts.Token;
                Now={$state.now};Sleep={param($seconds,$token) $state.delay+=$seconds;$state.now=$state.now.AddSeconds($seconds)};
                Transport={param($context,$method,$uri,$body,$deadline)
                    $state.calls++
                    switch($env:FOUNDRY_SCENARIO) {
                        deadline {$state.now=$state.now.AddSeconds(40);return @{statusCode=200}}
                        terminal {return @{statusCode=403}}
                        mutation503 {return @{statusCode=503}}
                        invalid-response {throw 'InvalidResponse'}
                        default {if($state.calls -eq 1){return @{statusCode=429;retryAfter=12}};return @{statusCode=200}}
                    }
                }}
            $path='deviceManagement/windowsAutopilotDeviceIdentities/id'
            if($env:FOUNDRY_SCENARIO -eq 'cancel') {$cts.Cancel()}
            if($env:FOUNDRY_SCENARIO -eq 'foreign') {$path='https://untrusted.invalid/steal'}
            $failure=$null
            $method=if($env:FOUNDRY_SCENARIO -eq 'mutation503'){'POST'}else{'GET'}
            try {$response=Invoke-FoundryAutopilotRequest $context $method $path $null ($state.now.AddSeconds(30))}
            catch {$failure=$_.Exception.Message}
            switch($env:FOUNDRY_SCENARIO) {
                deadline {if($failure -ne 'TimedOut' -or $state.calls -ne 1){throw 'Late response accepted.'}}
                cancel {if(-not $failure -or $state.calls -ne 0){throw 'Cancelled operation sent HTTP.'}}
                foreign {if($failure -ne 'InvalidGraphEndpoint' -or $state.calls -ne 0){throw 'Foreign endpoint accepted.'}}
                terminal {if($response.statusCode -ne 403 -or $state.calls -ne 1){throw 'Terminal response retried.'}}
                mutation503 {if($response.statusCode -ne 503 -or $state.calls -ne 1){throw 'Ambiguous mutation retried.'}}
                invalid-response {if($failure -ne 'InvalidResponse' -or $state.calls -ne 1){throw 'Malformed response retried.'}}
                retry-after {if($response.statusCode -ne 200 -or $state.calls -ne 2 -or $state.delay -ne 12){throw 'Retry-After not respected.'}}
            }
            $cts.Dispose()
            """, new() { ["FOUNDRY_SCENARIO"] = scenario });
    }

    public static IEnumerable<object[]> Fixtures => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Autopilot"), "*.json")
        .Select(path => new object[] { Path.GetFileNameWithoutExtension(path) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Import_UsesAuthoritativeIdentityFromSharedFixture(string fixture)
    {
        await RunAsync("""
            $fixture=Get-Content -LiteralPath $env:FOUNDRY_FIXTURE -Raw | ConvertFrom-Json
            $state=@{poll=0;device=0;updates=0;now=[DateTimeOffset]::UtcNow}
            $context=@{Config=@{graphBaseUri='https://graph.microsoft.com/v1.0'};Token=[Threading.CancellationToken]::None;
                Now={ $state.now }; Sleep={param($seconds,$token) $state.now=$state.now.AddSeconds($seconds)};
                Transport={param($context,$method,$uri,$body,$deadline)
                    if($uri -match '/import$') { return @{statusCode=200;body=$fixture.importResponse} }
                    if($uri -match '/importedWindowsAutopilotDeviceIdentities/imported-current$') {
                        $row=$fixture.importedPolls[$state.poll];$state.poll++; return @{statusCode=200;body=$row}
                    }
                    if($uri -match '/windowsAutopilotDeviceIdentities/registration-current/updateDeviceProperties$') {
                        $state.updates++; return @{statusCode=204;body=$null}
                    }
                    if($uri -match '/windowsAutopilotDeviceIdentities/registration-current$') {
                        $row=$fixture.deviceResponses[$state.device];$state.device++; return $row
                    }
                    throw 'Unexpected identity route.'
                }}
            $result=Invoke-FoundryAutopilotImport $context $fixture.request
            if($result.status -cne $fixture.expected.status -or $result.code -cne $fixture.expected.code) {
                throw ('Wrong outcome: '+($result|ConvertTo-Json -Compress))
            }
            if($state.updates -ne $fixture.expected.propertyUpdateCount) { throw 'Wrong property update count.' }
            if($result.registrationId -cne $fixture.expected.registrationId) { throw 'Wrong registration ID.' }
            """, new() { ["FOUNDRY_FIXTURE"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Autopilot", fixture + ".json") });
    }

    private static async Task RunAsync(string harness, Dictionary<string, string?>? environment = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryAutopilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string protocol = Path.Combine(root, "protocol.ps1");
            using Stream resource = typeof(PreOobeScriptResources).Assembly.GetManifestResourceStream("Foundry.Deploy.AutopilotRegistration.Foundry-AutopilotProtocol.ps1")!;
            using (FileStream output = File.Create(protocol)) resource.CopyTo(output);
            environment ??= new();
            environment["FOUNDRY_PROTOCOL"] = protocol;
            harness = "$ErrorActionPreference='Stop'\n. $env:FOUNDRY_PROTOCOL\n" + harness;
            var request = new ProcessExecutionRequest(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(harness))], root)
            { ExecutionTimeout = TimeSpan.FromSeconds(30), EnvironmentOverrides = environment };
            ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess, result.ToDiagnosticText());
        }
        finally { Directory.Delete(root, true); }
    }
}
