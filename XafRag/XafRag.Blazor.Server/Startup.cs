using DevExpress.AIIntegration;
using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Security.Authentication;
using DevExpress.ExpressApp.Security.Authentication.ClientServer;
using DevExpress.ExpressApp.WebApi.Services;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using OpenAI;
using System.Text;
using XafRag.Blazor.Server.Configuration;
using XafRag.Blazor.Server.Services;
using XafRag.Module.BusinessObjects;
using XafRag.WebApi.JWT;

namespace XafRag.Blazor.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // https://www.npgsql.org/doc/types/datetime.html#timestamps-and-timezones
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            // RagDbContext for vector operations (separate from XAF's DbContext)
            var ragConnectionString = Configuration.GetConnectionString("ConnectionString")!
                .Replace("EFCoreProvider=Postgres;", "");
            var npgsqlDataSourceBuilder = new NpgsqlDataSourceBuilder(ragConnectionString);
            npgsqlDataSourceBuilder.UseVector();
            var npgsqlDataSource = npgsqlDataSourceBuilder.Build();

            services.AddDbContext<RagDbContext>((sp, options) =>
            {
                options.UseNpgsql(npgsqlDataSource, o => o.UseVector());
            });

            // Configuration
            services.Configure<OpenAiOptions>(Configuration.GetSection(OpenAiOptions.SectionName));
            services.Configure<RagOptions>(Configuration.GetSection(RagOptions.SectionName));

            // OpenAI clients
            var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
            var openAiOptions = Configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>()!;
            var openAiClient = new OpenAIClient(openAiApiKey);

            // Chat client (for DxAIChat and RagService)
            IChatClient chatClient = openAiClient
                .GetChatClient(openAiOptions.ChatModel)
                .AsIChatClient();
            services.AddChatClient(chatClient);

            // Embedding generator
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = openAiClient
                .GetEmbeddingClient(openAiOptions.EmbeddingModel)
                .AsIEmbeddingGenerator();
            services.AddSingleton(embeddingGenerator);

            // DevExpress AI services
            services.AddDevExpressBlazor();
            services.AddDevExpressAI();

            // RAG services
            services.AddScoped<ChunkingService>();
            services.AddScoped<EmbeddingService>();
            services.AddScoped<DocumentProcessingService>();
            services.AddScoped<RagService>();
            services.AddSingleton<IngestionService>();

            services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.HubConnectionHandler<>), typeof(ProxyHubConnectionHandler<>));

            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddHttpContextAccessor();
            services.AddScoped<IAuthenticationTokenProvider, JwtTokenProviderService>();
            services.AddScoped<CircuitHandler, CircuitHandlerProxy>();
            services.AddXaf(Configuration, builder =>
            {
                builder.UseApplication<XafRagBlazorApplication>();

                builder.AddXafWebApi(webApiBuilder =>
                {
                    webApiBuilder.ConfigureOptions(options =>
                    {
                        // Make your business objects available in the Web API and generate the GET, POST, PUT, and DELETE HTTP methods for it.
                        // options.BusinessObject<YourBusinessObject>();
                    });
                });

                builder.Modules
                    .AddConditionalAppearance()
                    .AddFileAttachments()
                    .AddNotifications()
                    .AddOffice()
                    .AddReports(options =>
                    {
                        options.EnableInplaceReports = true;
                        options.ReportDataType = typeof(DevExpress.Persistent.BaseImpl.EF.ReportDataV2);
                        options.ReportStoreMode = DevExpress.ExpressApp.ReportsV2.ReportStoreModes.XML;
                    })
                    .AddValidation(options =>
                    {
                        options.AllowValidationDetailsAccess = false;
                    })
                    .AddViewVariants()
                    .Add<XafRag.Module.XafRagModule>()
                    .Add<XafRagBlazorModule>();
                builder.ObjectSpaceProviders
                    .AddSecuredEFCore(options =>
                    {
                        options.PreFetchReferenceProperties();
                    })
                    .WithDbContext<XafRag.Module.BusinessObjects.XafRagEFCoreDbContext>((serviceProvider, options) =>
                    {
                        // Uncomment this code to use an in-memory database. This database is recreated each time the server starts. With the in-memory database, you don't need to make a migration when the data model is changed.
                        // Do not use this code in production environment to avoid data loss.
                        // We recommend that you refer to the following help topic before you use an in-memory database: https://docs.microsoft.com/en-us/ef/core/testing/in-memory
                        //options.UseInMemoryDatabase();
                        string connectionString = null;
                        if (Configuration.GetConnectionString("ConnectionString") != null)
                        {
                            connectionString = Configuration.GetConnectionString("ConnectionString");
                        }
#if EASYTEST
                        if(Configuration.GetConnectionString("EasyTestConnectionString") != null) {
                            connectionString = Configuration.GetConnectionString("EasyTestConnectionString");
                        }
#endif
                        ArgumentNullException.ThrowIfNull(connectionString);
                        options.UseConnectionString(connectionString);
                    })
                    .AddNonPersistent();
                builder.Security
                    .UseIntegratedMode(options =>
                    {
                        options.Lockout.Enabled = true;

                        options.RoleType = typeof(PermissionPolicyRole);
                        // ApplicationUser descends from PermissionPolicyUser and supports the OAuth authentication. For more information, refer to the following topic: https://docs.devexpress.com/eXpressAppFramework/402197
                        // If your application uses PermissionPolicyUser or a custom user type, set the UserType property as follows:
                        options.UserType = typeof(XafRag.Module.BusinessObjects.ApplicationUser);
                        // ApplicationUserLoginInfo is only necessary for applications that use the ApplicationUser user type.
                        // If you use PermissionPolicyUser or a custom user type, comment out the following line:
                        options.UserLoginInfoType = typeof(XafRag.Module.BusinessObjects.ApplicationUserLoginInfo);
                        options.Events.OnSecurityStrategyCreated += securityStrategy =>
                        {
                            // Use the 'PermissionsReloadMode.NoCache' option to load the most recent permissions from the database once
                            // for every DbContext instance when secured data is accessed through this instance for the first time.
                            // Use the 'PermissionsReloadMode.CacheOnFirstAccess' option to reduce the number of database queries.
                            // In this case, permission requests are loaded and cached when secured data is accessed for the first time
                            // and used until the current user logs out.
                            // See the following article for more details: https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Security.SecurityStrategy.PermissionsReloadMode.
                            ((SecurityStrategy)securityStrategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                        };
                    })
                    .AddPasswordAuthentication(options =>
                    {
                        options.IsSupportChangePassword = true;
                    });
            });
            var authentication = services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
            authentication.AddCookie(options =>
            {
                options.LoginPath = "/LoginPage";
            });
            authentication.AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateIssuerSigningKey = true,
                    //ValidIssuer = Configuration["Authentication:Jwt:Issuer"],
                    //ValidAudience = Configuration["Authentication:Jwt:Audience"],
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Authentication:Jwt:IssuerSigningKey"])),
                    AuthenticationType = JwtBearerDefaults.AuthenticationScheme
                };
            });
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme)
                        .RequireAuthenticatedUser()
                        .RequireXafAuthentication()
                        .Build();
            });

            services
                .AddControllers()
                .AddOData((options, serviceProvider) =>
                {
                    options
                        .AddRouteComponents("api/odata", new EdmModelBuilder(serviceProvider).GetEdmModel(), Microsoft.OData.ODataVersion.V401, _routeServices =>
                        {
                            _routeServices.ConfigureXafWebApiServices();
                        })
                        .EnableQueryFeatures(100);
                });

            services.AddSwaggerGen(c =>
            {
                c.EnableAnnotations();
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "XafRag API",
                    Version = "v1",
                    Description = @"Use AddXafWebApi(options) in the XafRag.Blazor.Server\Startup.cs file to make Business Objects available in the Web API."
                });
                c.AddSecurityDefinition("JWT", new OpenApiSecurityScheme()
                {
                    Type = SecuritySchemeType.Http,
                    Name = "Bearer",
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement() {
                    {
                        new OpenApiSecurityScheme() {
                            Reference = new OpenApiReference() {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "JWT"
                            }
                        },
                        new string[0]
                    },
                });
            });

            services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
            {
                //The code below specifies that the naming of properties in an object serialized to JSON must always exactly match
                //the property names within the corresponding CLR type so that the property names are displayed correctly in the Swagger UI.
                //XPO is case-sensitive and requires this setting so that the example request data displayed by Swagger is always valid.
                //Comment this code out to revert to the default behavior.
                //See the following article for more information: https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.propertynamingpolicy
                o.JsonSerializerOptions.PropertyNamingPolicy = null;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "XafRag WebApi v1");
                });
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. To change this for production scenarios, see: https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseRequestLocalization();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.UseXaf();
            // Ensure RAG database schema exists
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
                ragDb.Database.EnsureCreated();
            }
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapXafEndpoints();
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
                endpoints.MapControllers();
            });
        }
    }
}
