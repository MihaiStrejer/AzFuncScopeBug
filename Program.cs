using System;
using System.Threading.Tasks;
using AzFuncScopeBug;
using DryIoc.Microsoft.DependencyInjection;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: WebJobsStartup(typeof(Program))]
namespace AzFuncScopeBug
{
    public class Program : IWebJobsStartup
    {
        public static IServiceProvider DryIocSideContainer { get; private set; }
        public static IServiceProvider DefaultDotNetContainer { get; private set; }

        public void Configure(IWebJobsBuilder builder)
        {
            Console.ResetColor();

            // Setup the Az Func container
            ConfigureServices(builder.Services);

            // Setup the DryIoc side container
            var dryServiceCollection = new ServiceCollection();
            ConfigureServices(dryServiceCollection);
            DryIocSideContainer = DryIocAdapter.Create(dryServiceCollection);

            // Setup default .net core side container
            var defaultServiceCollection = new ServiceCollection();
            ConfigureServices(defaultServiceCollection);
            DefaultDotNetContainer = defaultServiceCollection.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Register mediatR
            services.AddScoped<IMediator, Mediator>();
            services.AddScoped<ServiceFactory>(pr => pr.GetRequiredService);

            // Add services
            services.AddTransient<ITestService, TestService>();
            services.AddTransient<IRequestHandler<MedRequest, Unit>, MedRequestHandler>();
        }
    }

    // Service
    public interface ITestService
    {
        Task Do();
    }
    public class TestService : ITestService
    {
        private readonly IServiceProvider _provider;
        private readonly IMediator _mediator;

        public TestService(IMediator mediator, IServiceProvider provider)
        {
            _mediator = mediator;
            _provider = provider;
        }

        public async Task Do()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Service] Provider: {_provider.GetHashCode()}");
            Console.ResetColor();

            await _mediator.Send(new MedRequest());
        }
    }

    // MediatR
    public class MedRequest : IRequest { }
    public class MedRequestHandler : RequestHandler<MedRequest>
    {
        private readonly IServiceProvider _provider;

        public MedRequestHandler(IServiceProvider provider)
        {
            _provider = provider;
        }

        protected override void Handle(MedRequest request)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[MediatR] Provider: {_provider.GetHashCode()}");
            Console.ResetColor();
        }
    }

    // Test Functions
    public class Functions
    {
        private readonly IServiceProvider _serviceProvider;

        public Functions(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Func] Provider: {_serviceProvider.GetHashCode()}");
            Console.ResetColor();
        }

        [FunctionName("Function1")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            await RunTest("Func container", _serviceProvider);
            await RunTest("DryIoc side container", Program.DryIocSideContainer);
            await RunTest(".NET container", Program.DefaultDotNetContainer);

            return new OkResult();
        }

        private static async Task RunTest(string testName, IServiceProvider provider)
        {
            using (var scope = provider.CreateScope())
            {
                var scopeProvider = scope.ServiceProvider;
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"---  {testName} - scoped provider: {scopeProvider.GetHashCode()} ---");
                Console.ResetColor();

                var testService = scopeProvider.GetService<ITestService>();
                await testService.Do();
            }
        }
    }
}