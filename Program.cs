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

            // Stateful scoped service
            services.AddScoped<MyStateService>();
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
        private readonly MyStateService _state;

        public TestService(IMediator mediator, IServiceProvider provider, MyStateService state)
        {
            _mediator = mediator;
            _provider = provider;
            _state = state;
        }

        public async Task Do()
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"{"[TestService] Provider:",-42}  {_provider.GetHashCode()} with state: {_state.GetHashCode()} -> {_state}");
            Console.ResetColor();

            await _mediator.Send(new MedRequest());
        }
    }

    // MediatR
    public class MedRequest : IRequest { }
    public class MedRequestHandler : RequestHandler<MedRequest>
    {
        private readonly IServiceProvider _provider;
        private readonly MyStateService _state;

        public MedRequestHandler(IServiceProvider provider, MyStateService state)
        {
            _provider = provider;
            _state = state;
        }

        protected override void Handle(MedRequest request)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"{"[MediatR] Provider:",-43} {_provider.GetHashCode()} with state: {_state.GetHashCode()} -> {_state}");
            Console.ResetColor();
        }
    }

    // Scope stateful service
    public class MyStateService
    {
        public string State { get; set; } = "I should not be here!";
        public int Count { get; set; }

        public override string ToString() => $"{State} {Count}";
    }

    // Test Functions
    public class Functions
    {
        private readonly IServiceProvider _serviceProvider;

        public Functions(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [FunctionName("Function1")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            // Az
            {
                await RunTest("FuncScope", _serviceProvider);

                //      Sub scope
                using var azFuncSubScope = _serviceProvider.CreateScope();
                await RunTest("FuncSubScope", azFuncSubScope.ServiceProvider);

                CheckScope(".NET", _serviceProvider);
            }

            // DryIoc
            {
                using var dryIocScope = Program.DryIocSideContainer.CreateScope();
                await RunTest("DryIoc", dryIocScope.ServiceProvider);

                //      Sub scope
                using var subDryIocScope = dryIocScope.ServiceProvider.CreateScope();
                await RunTest("DryIocSubScope", subDryIocScope.ServiceProvider);

                CheckScope("DryIoc", dryIocScope.ServiceProvider);
            }

            // Dot Net
            {
                using var defaultDotNetScope = Program.DefaultDotNetContainer.CreateScope();
                await RunTest(".NET", defaultDotNetScope.ServiceProvider);

                //      Sub scope
                using var subDefaultDotNetScope = defaultDotNetScope.ServiceProvider.CreateScope();
                await RunTest(".NETSubScope", subDefaultDotNetScope.ServiceProvider);

                CheckScope(".NET", defaultDotNetScope.ServiceProvider);
            }

            return new OkResult();
        }

        private static async Task RunTest(string testName, IServiceProvider provider)
        {
            //var scopeProvider = provider;

            // Create a scoped state and set some data
            var state = provider.GetService<MyStateService>();
            state.State = testName;
            state.Count++;

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"---  {testName,-28} provider: {provider.GetHashCode()} with state: {state.GetHashCode()} ---");
            Console.ResetColor();

            var testService = provider.GetService<ITestService>();
            await testService.Do();
        }

        private static void CheckScope(string expectedScope, IServiceProvider provider)
        {
            var state = provider.GetService<MyStateService>();
            if (expectedScope.Equals(state.State))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Final state check Ok, scope state: {state}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Final state check FAILED, scope state: {state}");
                Console.ResetColor();
            }
        }
    }
}