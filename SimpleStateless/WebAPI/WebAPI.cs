using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ApplicationInsights;

namespace WebAPI
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class WebAPI : StatelessService
    {
        private StatelessServiceContext mycontext;

        public WebAPI(StatelessServiceContext context)
            : base(context)
        {
            mycontext = context;
        }
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            //int mycount = 0;
            // TODO: Replace the following sample code with your own logic 
            //       or remove this RunAsync override if it's not needed in your service.
            //TelemetryClient myClient = new TelemetryClient();
            ServiceEventSource.Current.Message("RunAsync called in service instance {0}",mycontext.InstanceId);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                #region Custom Metrics
                // continue increasing to 20 minutes
                //while(mycount <= 1200)
                //{
                //    myClient.TrackMetric("CountNumServices", mycount);
                //    mycount++;
                //    ServiceEventSource.Current.Message("mycount increments to {0}", mycount.ToString());
                //    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                //}

                //// 5 minutesOnce the count gets to be 100K, count down to 1000
                //while(mycount >= 300)
                //{
                //    myClient.TrackMetric("CountNumServices", mycount);
                //    mycount--;
                //    ServiceEventSource.Current.Message("mycount decrements to {0}", mycount.ToString());
                //    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                //}
                #endregion

            }
        }
        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatelessServiceContext>(serviceContext))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseApplicationInsights()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }
    }
}
