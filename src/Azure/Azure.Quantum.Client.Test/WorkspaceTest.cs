﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Quantum;
using Azure.Quantum.Jobs;
using Azure.Quantum.Jobs.Models;

using Microsoft.Azure.Quantum.Exceptions;
using Microsoft.Azure.Quantum.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Azure.Quantum.Test
{
    [TestClass]
    public class WorkspaceTest
    {
        private const string SETUP = @"
Live tests require you to configure your environment with these variables:
  * AZURE_QUANTUM_WORKSPACE_NAME: the name of an Azure Quantum workspace to use for live testing.
  * AZURE_QUANTUM_SUBSCRIPTION_ID: the Azure Quantum workspace's Subscription Id.
  * AZURE_QUANTUM_WORKSPACE_RG: the Azure Quantum workspace's resource group.
  * AZURE_QUANTUM_WORKSPACE_LOCATION: the Azure Quantum workspace's location (region).

We'll also try to authenticate with Azure using an instance of DefaultCredential. See
https://docs.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme#authenticate-the-client
for details.

Tests will be marked as Inconclusive if the pre-reqs are not correctly setup.";

        [TestMethod]
        [TestCategory("Live")]
        public async Task SubmitJobTest()
        {
            // Create Job
            IWorkspace workspace = GetLiveWorkspace();

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(30000);

            var job = await SubmitTestProblem(workspace);
            AssertJob(job);

            await job.WaitForCompletion(cancellationToken: cts.Token);

            AssertJob(job);
            Assert.IsTrue(job.Succeeded);
        }

        [TestMethod]
        [TestCategory("Live")]
        public async Task GetJobTest()
        {
            IWorkspace workspace = GetLiveWorkspace();

            // Since this is a live workspace, we don't have much control about what jobs are in there
            // Get the jobs, and call Get on the first.
            await foreach (var job in workspace.ListJobsAsync())
            {
                AssertJob(job);

                var current = workspace.GetJob(job.Id);
                AssertJob(current);
                Assert.AreEqual(job.Id, current.Id);

                break;
            }
        }

        [TestMethod]
        [TestCategory("Live")]
        public async Task CancelJobTest()
        {
            // Create Job
            IWorkspace workspace = GetLiveWorkspace();

            var job = await SubmitTestProblem(workspace);
            AssertJob(job);

            try
            {
                var result = workspace.CancelJob(job.Id);
                AssertJob(result);
            }
            catch (WorkspaceClientException e)
            {
                Assert.AreEqual((int)HttpStatusCode.Conflict, e.Status);
            }
        }

        [TestMethod]
        [TestCategory("Live")]
        public async Task ListJobsTest()
        {
            IWorkspace workspace = GetLiveWorkspace();
            int max = 3;

            // Since this is a live workspace, we don't have much control about what jobs are in there
            // Just make sure there is more than one.
            await foreach (var job in workspace.ListJobsAsync())
            {
                Assert.IsNotNull(job);
                Assert.IsNotNull(job.Details);
                Assert.IsNotNull(job.Workspace);
                Assert.IsFalse(string.IsNullOrWhiteSpace(job.Id));
                Assert.AreEqual(job.Details.Id, job.Id);

                max--;
                if (max <= 0)
                {
                    break;
                }
            }

            // Make sure we iterated through all the expected jobs:
            Assert.AreEqual(0, max);
        }

        [TestMethod]
        [TestCategory("Live")]
        public async Task ListQuotasTest()
        {
            IWorkspace workspace = GetLiveWorkspace();
            int max = 3;

            // Since this is a live workspace, we don't have much control about what quotas are in there
            // Just make sure there is more than one.
            await foreach (var q in workspace.ListQuotasAsync())
            {
                Assert.IsNotNull(q);
                Assert.IsNotNull(q.ProviderId);
                Assert.IsNotNull(q.Dimension);

                max--;
                if (max <= 0)
                {
                    break;
                }
            }

            // Make sure we iterated through all the expected jobs:
            Assert.AreEqual(0, max);
        }

        [TestMethod]
        [TestCategory("Live")]
        public async Task ListProviderStatusTest()
        {
            IWorkspace workspace = GetLiveWorkspace();
            int max = 1;

            // Since this is a live workspace, we don't have much control about what quotas are in there
            // Just make sure there is more than one.
            await foreach (var s in workspace.ListProvidersStatusAsync())
            {
                Assert.IsNotNull(s);
                Assert.IsNotNull(s.ProviderId);
                Assert.IsNotNull(s.Targets);
                Assert.IsTrue(s.Targets.Any());

                max--;
                if (max <= 0)
                {
                    break;
                }
            }

            // Make sure we iterated through all the expected jobs:
            Assert.AreEqual(0, max);
        }

        [TestMethod]
        [TestCategory("Local")]
        public async Task ApplicationIdTest()
        {
            const string ENV_VAR_APPID = "EnvVarAppId";
            const string OPTIONS_APPID = "OptionAppId";
            const string LONG_ENV_VAR_APPID = "LongEnvVarAppId";
            const string LONG_OPTIONS_APPID = "LongOptionAppId";
            const string VERY_LONG_ENV_VAR_APPID = "VeryVeryVeryVeryVeryVeryLongEnvVarAppId";
            const string VERY_LONG_OPTIONS_APPID = "VeryVeryVeryVeryVeryVeryLongOptionAppId";
            const string APPID_ENV_VAR_NAME = "AZURE_QUANTUM_NET_APPID";

            Func<QuantumJobClientOptions, Workspace> createWorkspace = (QuantumJobClientOptions options) =>
            {
                var credential = new ClientSecretCredential(tenantId: "72f988bf-86f1-41af-91ab-2d7cd011db47", 
                                                            clientId: "00000000-0000-0000-0000-000000000000",
                                                            clientSecret: "PLACEHOLDER");                                                        
                return new Workspace(subscriptionId: "SubscriptionId",
                                     resourceGroupName: "ResourceGroupName",
                                     workspaceName: "WorkspaceName",
                                     location: "WestUs",
                                     options: options,
                                     credential: credential);
            };

            var originalEnvironmentAppId = Environment.GetEnvironmentVariable(APPID_ENV_VAR_NAME);
            try
            {

                // Test with no Environment AppId and no Options AppId
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, null);
                var workspace = createWorkspace(null);
                Assert.IsNotNull(workspace.ClientOptions);
                Assert.IsNotNull(workspace.ClientOptions.Diagnostics);
                Assert.AreEqual("", workspace.ClientOptions.Diagnostics.ApplicationId);

                // Test with Environment AppId and no Options AppId
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, ENV_VAR_APPID);
                workspace = createWorkspace(null);
                Assert.IsNotNull(workspace.ClientOptions);
                Assert.IsNotNull(workspace.ClientOptions.Diagnostics);
                Assert.AreEqual(ENV_VAR_APPID, workspace.ClientOptions.Diagnostics.ApplicationId);

                // Test with no Environment AppId and with Options AppId 
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, null);
                var options = new QuantumJobClientOptions();
                options.Diagnostics.ApplicationId = OPTIONS_APPID;
                workspace = createWorkspace(options);
                Assert.IsNotNull(workspace.ClientOptions);
                Assert.IsNotNull(workspace.ClientOptions.Diagnostics);
                Assert.AreEqual(OPTIONS_APPID, workspace.ClientOptions.Diagnostics.ApplicationId);

                // Test with Environment AppId and with Options AppId
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, ENV_VAR_APPID);
                options = new QuantumJobClientOptions();
                options.Diagnostics.ApplicationId = OPTIONS_APPID;
                workspace = createWorkspace(options);
                Assert.IsNotNull(workspace.ClientOptions);
                Assert.IsNotNull(workspace.ClientOptions.Diagnostics);
                Assert.AreEqual($"{OPTIONS_APPID}-{ENV_VAR_APPID}", workspace.ClientOptions.Diagnostics.ApplicationId);

                // Test with long (>24 chars) combination of Environment AppId and Options AppId
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, LONG_ENV_VAR_APPID);
                options = new QuantumJobClientOptions();
                options.Diagnostics.ApplicationId = LONG_OPTIONS_APPID;
                workspace = createWorkspace(options);
                Assert.IsNotNull(workspace.ClientOptions);
                Assert.IsNotNull(workspace.ClientOptions.Diagnostics);
                var truncatedAppId = $"{LONG_OPTIONS_APPID}-{LONG_ENV_VAR_APPID}".Substring(0, 24);
                Assert.AreEqual(truncatedAppId, workspace.ClientOptions.Diagnostics.ApplicationId);

                // Test with long (>24 chars) Environment AppId and no Options AppId
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, VERY_LONG_ENV_VAR_APPID);
                workspace = createWorkspace(null);
                Assert.IsNotNull(workspace.ClientOptions);
                Assert.IsNotNull(workspace.ClientOptions.Diagnostics);
                Assert.AreEqual(VERY_LONG_ENV_VAR_APPID.Substring(0, 24), workspace.ClientOptions.Diagnostics.ApplicationId);

                // Test with long (>24 chars) Options AppId and no Environment AppId
                Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, null);
                options = new QuantumJobClientOptions();
                Assert.ThrowsException<System.ArgumentOutOfRangeException>(() => 
                    options.Diagnostics.ApplicationId = VERY_LONG_OPTIONS_APPID);
            }
            finally
            {
                // restore original env var AZURE_QUANTUM_NET_APPID
                if (originalEnvironmentAppId != null)
                {
                    Environment.SetEnvironmentVariable(APPID_ENV_VAR_NAME, originalEnvironmentAppId);
                }
            }
        }

        private static void AssertJob(CloudJob job)
        {
            Assert.IsNotNull(job);
            Assert.IsNotNull(job.Details);
            Assert.IsNotNull(job.Workspace);
            Assert.IsFalse(string.IsNullOrEmpty(job.Id));
            Assert.AreEqual(job.Id, job.Details.Id);
        }

        private IWorkspace GetLiveWorkspace()
        {
            if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_SUBSCRIPTION_ID")) ||
                string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_WORKSPACE_RG")) ||
                string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_WORKSPACE_NAME")) ||
                string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_SUBSCRIPTION_ID")))
            {
                Assert.Inconclusive(SETUP);
            }

            var options = new QuantumJobClientOptions();
            options.Diagnostics.ApplicationId = Environment.GetEnvironmentVariable("AZURE_QUANTUM_NET_APPID") ?? "ClientTests";

            var credential = Authentication.CredentialFactory.CreateCredential(Authentication.CredentialType.Default);

            return new Workspace(
                subscriptionId: System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_SUBSCRIPTION_ID"),
                resourceGroupName: System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_WORKSPACE_RG"),
                workspaceName: System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_WORKSPACE_NAME"),
                location: System.Environment.GetEnvironmentVariable("AZURE_QUANTUM_WORKSPACE_LOCATION"),
                options: options,
                credential: credential);
        }

        private static JobDetails CreateJobDetails(string jobId, string containerUri = null, string inputUri = null)
        {
            return new JobDetails(
                containerUri: containerUri,
                inputDataFormat: "microsoft.qio.v2",
                providerId: "Microsoft",
                target: "microsoft.paralleltempering-parameterfree.cpu")
            {
                Id = jobId,
                Name = "Azure.Quantum.Unittest",
                OutputDataFormat = "microsoft.qio-results.v2",
                InputParams = new Dictionary<string, object>()
                {
                    { "params", new Dictionary<string, object>() },
                },
                InputDataUri = inputUri,
            };
        }

        private static Problem CreateTestProblem()
        {
            // Create an Ising-type problem for shipping-containers
            var containerWeights = new int[] { 1, 5, 9, 21, 35, 5, 3, 5, 10, 11 };

            var problem = new Problem(ProblemType.Ising);
            for (int i = 0; i < containerWeights.Length; i++)
            {
                for (int j = 0; j < containerWeights.Length; j++)
                {
                    if (i != j)
                    {
                        problem.Add(i, j, containerWeights[i] * containerWeights[j]);
                    }
                }
            }

            return problem;
        }

        private async Task<(string, string)> UploadProblem(IWorkspace workspace, Problem problem, string jobId)
        {
            string intermediaryFile = Path.GetTempFileName();

            // Save to the intermediary file
            using (var intermediaryWriter = File.Create(intermediaryFile))
            {
                using (var compressionStream = new GZipStream(intermediaryWriter, CompressionLevel.Fastest))
                {
                    await problem.SerializeAsync(compressionStream);
                }
            }

            using (var intermediaryReader = File.OpenRead(intermediaryFile))
            {
                var jobStorageHelper = new LinkedStorageJobHelper(workspace);
                return await jobStorageHelper.UploadJobInputAsync(jobId, intermediaryReader);
            }
        }

        private async Task<CloudJob> SubmitTestProblem(IWorkspace workspace)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(30000);

            var jobId = Guid.NewGuid().ToString();
            var problem = CreateTestProblem();

            // Upload problem:
            var (containerUri, inputUri) = await UploadProblem(workspace, problem, jobId);

            CloudJob src = new CloudJob(workspace, CreateJobDetails(jobId, containerUri, inputUri));
            AssertJob(src);

            var job = await workspace.SubmitJobAsync(src, cts.Token);
            AssertJob(job);
            Assert.AreEqual(jobId, job.Id);
            Assert.IsFalse(job.Failed);

            return job;
        }
    }
}
