using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Interfaces.CreditNoteInterface;
using AgentSyncConsole.Interfaces.PaymentInterface;
using AgentSyncConsole.Interfaces.PosInvoiceInterface;
using AgentSyncConsole.Interfaces.TransactionInterface;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.Repositories;
using AgentSyncConsole.Repositories.CreditNoteRepo;
using AgentSyncConsole.Repositories.PaymentRepo;
using AgentSyncConsole.Repositories.PosInvoiceRepo;
using AgentSyncConsole.Services;
using AgentSyncConsole.Services.PosInvoiceServices;
using AgentSyncConsole.Utilites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using static System.Runtime.InteropServices.JavaScript.JSType;
// Fully-qualified aliases for the merged Customer Books Sync module
// (formerly the standalone CustomerBooksSync.Api ASP.NET project), kept in
// its own namespace so its ICustomerRepository / IAccessTokenRepository-shape
// concerns never collide with the pre-existing Agent/Corporate and Books
// Invoice Sync types of similar names.
using CustomerBooksConfig = AgentSyncConsole.CustomerBooks.Configuration;
using CustomerBooksInterfaces = AgentSyncConsole.CustomerBooks.Interfaces;
using CustomerBooksRepositories = AgentSyncConsole.CustomerBooks.Repositories;
using CustomerBooksServices = AgentSyncConsole.CustomerBooks.Services;
// Fully-qualified aliases for the merged Invoice-JSON-to-SQL module, kept in
// its own namespace so its IInvoiceRepository / IInvoiceLineItemRepository /
// IThirdPartyRepository (write/ingest shape) never collide with the
// pre-existing Books-flavor repositories of (almost) the same name.
using InvoiceIngestConfig = AgentSyncConsole.InvoiceIngest.Configuration;
using InvoiceIngestInterfaces = AgentSyncConsole.InvoiceIngest.Interfaces;
using InvoiceIngestRepositories = AgentSyncConsole.InvoiceIngest.Repositories;
using InvoiceIngestServices = AgentSyncConsole.InvoiceIngest.Services;

namespace AgentSyncConsole;

/// <summary>
/// Composition root ONLY. Loads configuration, registers every service from
/// all merged projects, builds the ServiceProvider, resolves IPipelineRunner,
/// and runs it. The actual execution order — Agent Sync -&gt; Corporate Sync -&gt;
/// Customer Books Sync -&gt; Invoice JSON -&gt; SQL -&gt; Books Invoice Sync — lives
/// in Services/PipelineRunner.cs, not here.
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/agentsync-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);

            await using var provider = services.BuildServiceProvider();

            var tokenApplication = configuration["ZohoAuth:TokenApplication"] ?? "Books";
            var accessTokenService = provider.GetRequiredService<IAccessTokenService>();
            await accessTokenService.InitializeAsync(tokenApplication);

            var pipeline = provider.GetRequiredService<IPipelineRunner>();
            var exitCode = await pipeline.RunAsync();

            Log.Information("Pipeline finished with exit code {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AgentSyncConsole terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(ServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        // Infrastructure
        services.AddSingleton<SqlConnectionFactory>();
        services.AddHttpClient("ZohoAccounts");
        services.AddHttpClient<IBooksApiService, BooksApiService>();
        services.AddHttpClient<CustomerBooksInterfaces.IZohoBooksApiClient, CustomerBooksServices.ZohoBooksApiClient>();

        // Execution timer - fresh instance per run (Reset() is called once per
        // page inside AgentCorporateSyncService, exactly like the original).
        services.AddScoped(_ => new ExecutionTimer(Constants.MAX_RUNTIME_MS));

        // ── Agent / Corporate sync repositories ──────────────────────────
        services.AddScoped<IPlaceOfSupplyRepository, PlaceOfSupplyRepository>();
        services.AddScoped<Interfaces.IThirdPartyRepository, ThirdPartyRepository>();
        services.AddScoped<IAgentCorporateCustomerRepository, CustomerRepository>();
        services.AddScoped<IGSTMasterRepository, GSTMasterRepository>();
        services.AddScoped<IOffsetManager, OffsetManager>();

        // ── Agent / Corporate sync services ──────────────────────────────
        services.AddScoped<IDuplicateCheckService, DuplicateCheckService>();
        services.AddScoped<IAgentSyncService, AgentSyncService>();
        services.AddScoped<ICorporateSyncService, CorporateSyncService>();
        services.AddScoped<IAgentCorporateSyncService, AgentCorporateSyncService>();

        // ── Books Invoice Sync (pre-existing feature) ────────────────────
        services.AddScoped<Interfaces.IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ILocationMasterRepository, LocationMasterRepository>();
        services.AddScoped<ITaxMasterRepository, TaxMasterRepository>();
        services.AddScoped<IItemMasterRepository, ItemMasterRepository>();
        services.AddScoped<Interfaces.IInvoiceLineItemRepository, InvoiceLineItemRepository>();
        services.AddSingleton<IAccessTokenRepository, AccessTokenRepository>();
        services.AddSingleton<IRetryService, RetryService>();
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        services.AddScoped<ICustomerRepository, CustomerRepositoryBooks>();
        services.AddScoped<IGSTService, GSTServiceBooks>();
        services.AddScoped<IBooksInvoiceSyncService, BooksInvoiceSyncService>();

        // ── Customer Books Sync (merged from CustomerBooksSync.Api, converted
        //    from an ASP.NET Web API project to plain console services) ───
        // NOTE: ICustomerRepository / IGstMasterRepository here are the
        // CustomerBooks-namespaced versions — do NOT confuse with the
        // pre-existing ICustomerRepository / IGSTMasterRepository registered
        // above for the Agent/Corporate track; both coexist safely because
        // each is bound to its own namespaced interface type.
        services.Configure<CustomerBooksConfig.CustomerBooksSettings>(
            configuration.GetSection(CustomerBooksConfig.CustomerBooksSettings.SectionName));
        services.AddScoped<CustomerBooksInterfaces.ICustomerRepository, CustomerBooksRepositories.CustomerRepository>();
        services.AddScoped<CustomerBooksInterfaces.IGstMasterRepository, CustomerBooksRepositories.GstMasterRepository>();
        services.AddScoped<CustomerBooksInterfaces.ICustomerBooksSyncService, CustomerBooksServices.CustomerBooksSyncService>();

        // ── Invoice JSON -> SQL (merged from InvoiceSync project) ────────
        services.Configure<InvoiceIngestConfig.SyncSettings>(
            configuration.GetSection(InvoiceIngestConfig.SyncSettings.SectionName));
        services.AddScoped<InvoiceIngestInterfaces.IDbConnectionFactory, InvoiceIngestRepositories.DbConnectionFactory>();
        services.AddScoped<InvoiceIngestInterfaces.IInvoiceRepository, InvoiceIngestRepositories.InvoiceRepository>();
        services.AddScoped<InvoiceIngestInterfaces.IInvoiceLineItemRepository, InvoiceIngestRepositories.InvoiceLineItemRepository>();
        services.AddScoped<InvoiceIngestInterfaces.IThirdPartyRepository, InvoiceIngestRepositories.ThirdPartyRepository>();
        services.AddScoped<InvoiceIngestInterfaces.IInvoiceSyncService, InvoiceIngestServices.InvoiceSyncService>();

        // ── Pipeline orchestrator: Agent -> Corporate -> Customer Books Sync -> Invoice JSON->SQL -> Books ──
        services.AddScoped<IPipelineRunner, PipelineRunner>();
        // Hotelogix Transaction Sync(converted from the Catalyst Transaction Sync function)
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ITransactionSyncService, TransactionSyncService>();

        // ThirdPartyData Services
        services.AddScoped<IThirdPartyDataRepository, ThirdPartyDataRepository>();

        //Mapping Payment service and repository with interfacce
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentService, PaymentService>();


        //Mapping Credit Note repository and service with their interfaces
        services.AddScoped<ICreditNoteSyncService_ZohoBooks,CreditNoteSyncService_ZohoBooks>();
        services.AddScoped<ICreditNoteSyncService, CreditNoteSyncService>();




        //Adding the PosInvoiceService
        services.AddScoped<IPosInvoiceRepository, PosInvoiceRepository>();
        services.AddScoped<IPosInvoiceLineItemRepository, PosInvoiceLineItemRepository>();
        services.AddScoped<IPosInvoiceService, PosInvoiceService>();
        services.AddScoped<IPosInvoiceBooksSyncService, PosInvoiceBooksSyncService>();

    }

}
