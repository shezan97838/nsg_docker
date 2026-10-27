using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>The two Grievance controllers over the real services and in-memory fakes: status codes, bodies, headers, attributes.</summary>
public class GrievanceControllerTests
{
    private static ControllerContext NewContext() => new() { HttpContext = new DefaultHttpContext() };

    private static (GrievancesController Controller, GrvKit Kit) Employee()
    {
        GrvKit kit = new();
        return (new GrievancesController(kit.Employees, kit.Categories) { ControllerContext = NewContext() }, kit);
    }

    private static (GrievanceAdminController Controller, GrvKit Kit) Admin(GrvKit? shared = null)
    {
        GrvKit kit = shared ?? new();
        return (new GrievanceAdminController(kit.Admins, kit.Categories, kit.Dashboard) { ControllerContext = NewContext() }, kit);
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        FileStreamResult => 200,
        _ => throw new InvalidOperationException(result.GetType().Name)
    };

    private static ProblemDetails ProblemOf(IActionResult result) => Assert.IsType<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static IFormFile FormFile(byte[] bytes, string name, string contentType)
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };

    private static CreateGrievanceForm Form(int categoryId, string note = "A reasonably long note.", IReadOnlyList<IFormFile>? files = null)
        => new() { CategoryId = categoryId, Subject = "Subject", Note = note, Files = files?.ToList() };

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ── Status code mapping ──

    [Fact]
    public async Task Creating_returns_201_with_a_message_and_both_the_numeric_id_and_the_ticket_grievanceId()
    {
        (GrievancesController controller, GrvKit kit) = Employee();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        IActionResult result = await controller.Create(Form(category.Id), default);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(GrievancesController.GetById), created.ActionName);
        ApiResult<GrievanceDetailDto> body = Assert.IsType<ApiResult<GrievanceDetailDto>>(created.Value);
        Assert.Contains("Grievance submitted successfully", body.Message);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(body, Web));
        JsonElement data = json.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("id").GetInt64() > 0);
        Assert.Matches(@"^NSG-\d{4}-\d{6}$", data.GetProperty("grievanceId").GetString());
        Assert.Equal(1, data.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Someone_elses_grievance_is_404_never_403_for_detail_and_download()
    {
        (GrievancesController controller, GrvKit kit) = Employee();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        Grievance theirs = kit.Repo.AddGrievance(GrvKit.OtherEmployee, category);

        IActionResult detail = await controller.GetById(theirs.Id, default);
        IActionResult download = await controller.DownloadDocument(theirs.Id, 0, default);

        Assert.All(new[] { detail, download }, r => Assert.Equal(404, StatusOf(r)));
        Assert.Equal(GrievanceMessages.GrievanceNotFound, ProblemOf(detail).Detail);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task Validation_problems_are_400_and_outages_503_each_with_a_readable_message()
    {
        (GrievancesController controller, GrvKit kit) = Employee();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        IActionResult shortNote = await controller.Create(Form(category.Id, "short"), default);
        IActionResult badBytes = await controller.Create(Form(category.Id, files: new[] { FormFile(GrvKit.PngBytes, "a.pdf", "application/pdf") }), default);
        IActionResult noCategory = await controller.Create(Form(9999), default);

        kit.Repo.TicketsUnavailable = true;
        IActionResult noTicket = await controller.Create(Form(category.Id), default);

        kit.Repo.TicketsUnavailable = false;
        kit.Availability.IsAvailable = false;
        IActionResult noStorage = await controller.Create(Form(category.Id, files: new[] { FormFile(GrvKit.PdfBytes, "a.pdf", "application/pdf") }), default);

        Assert.Equal(400, StatusOf(shortNote));
        Assert.Equal(400, StatusOf(badBytes));
        Assert.Equal(400, StatusOf(noCategory));
        Assert.Equal(503, StatusOf(noTicket));
        Assert.Equal(GrievanceErrorCodes.TicketUnavailable, ProblemOf(noTicket).Title);
        Assert.Equal(503, StatusOf(noStorage));
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, ProblemOf(noStorage).Title);
        Assert.All(new[] { shortNote, badBytes, noCategory, noTicket, noStorage }, r => Assert.False(string.IsNullOrWhiteSpace(ProblemOf(r).Detail)));
        Assert.All(new[] { shortNote, badBytes, noCategory, noTicket, noStorage }, r => Assert.NotNull(ProblemOf(r).Extensions["traceId"]));
    }

    [Fact]
    public async Task An_oversized_file_is_a_400_that_states_the_limit()
    {
        (GrievancesController controller, GrvKit kit) = Employee();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        byte[] big = new byte[5 * 1024 * 1024 + 1];
        GrvKit.PdfBytes.CopyTo(big, 0);

        IActionResult result = await controller.Create(Form(category.Id, files: new[] { FormFile(big, "big.pdf", "application/pdf") }), default);

        Assert.Equal(400, StatusOf(result));
        Assert.Contains("5 MB", ProblemOf(result).Detail);
        Assert.Empty(kit.Repo.Grievances);
    }

    [Fact]
    public async Task Creating_with_two_attachments_is_one_201_that_lists_them_without_keys_and_the_form_streams_are_released()
    {
        (GrievancesController controller, GrvKit kit) = Employee();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        IActionResult result = await controller.Create(Form(category.Id, files: new[]
        {
            FormFile(GrvKit.PdfBytes, "a.pdf", "application/pdf"),
            FormFile(GrvKit.JpegBytes, "b.jpg", "image/jpeg")
        }), default);

        CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(GrievancesController.GetById), created.ActionName);
        GrievanceDetailDto dto = Assert.IsType<ApiResult<GrievanceDetailDto>>(created.Value).Data!;
        Assert.Equal(new[] { "a.pdf", "b.jpg" }, dto.Documents.Select(d => d.Name));
        Assert.Equal(2, kit.Storage.StoreCalls.Count);
        Assert.DoesNotContain("grievances/", JsonSerializer.Serialize(created.Value, Web));
    }

    // ── No blank responses ──

    [Fact]
    public async Task Empty_answers_are_one_message_never_an_empty_list()
    {
        (GrievancesController employee, GrvKit kit) = Employee();
        (GrievanceAdminController admin, _) = Admin(kit);

        AssertMessage(await employee.GetCategories(default), GrievanceMessages.NoCategoriesFound);
        AssertMessage(await employee.GetMine(1, 20, null, default), GrievanceMessages.NoGrievancesFound);
        AssertMessage(await admin.GetAll(1, 20, null, null, null, null, null, null, default), GrievanceMessages.NoGrievancesFound);
        AssertMessage(await admin.GetCategories(1, 20, null, null, default), GrievanceMessages.NoCategoriesFound);
        AssertMessage(await admin.GetDashboardSummary(null, null, default), GrievanceMessages.NoDashboardData);

        static void AssertMessage(IActionResult result, string expected)
        {
            OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expected, Assert.IsType<ApiMessageResponse>(ok.Value).Message);
        }
    }

    [Fact]
    public async Task Single_item_reads_use_the_uniform_envelope_and_lists_are_paged()
    {
        (GrievancesController controller, GrvKit kit) = Employee();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category);

        var detail = Assert.IsType<ApiResponse<GrievanceDetailDto>>(Assert.IsType<OkObjectResult>(await controller.GetById(g.Id, default)).Value);
        var list = Assert.IsType<PaginatedResponse<GrievanceSummaryDto>>(Assert.IsType<OkObjectResult>(await controller.GetMine(1, 20, null, default)).Value);
        var categories = Assert.IsType<ApiResponse<IReadOnlyList<GrievanceCategoryDto>>>(Assert.IsType<OkObjectResult>(await controller.GetCategories(default)).Value);

        Assert.True(detail.Success);
        Assert.Equal(1, list.TotalCount);
        Assert.Single(categories.Data!);
    }

    [Fact]
    public async Task An_undefined_status_filter_is_a_400()
    {
        (GrievancesController controller, _) = Employee();
        Assert.Equal(400, StatusOf(await controller.GetMine(1, 20, (GrievanceStatus)9, default)));
    }

    // ── Downloads: headers, disposal, no ranges ──

    private static async Task<(DefaultHttpContext Http, byte[] Body)> ExecuteAsync(IActionResult result, string? rangeHeader = null)
    {
        DefaultHttpContext http = new()
        {
            RequestServices = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider()
        };
        if (rangeHeader != null) http.Request.Headers.Range = rangeHeader;
        MemoryStream body = new();
        http.Response.Body = body;
        await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        return (http, body.ToArray());
    }

    private static (GrvKit Kit, Grievance Grievance) WithDocument()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        GrievanceDocumentRecord doc = new("grievances/2026/10/55555555-5555-5555-5555-555555555555.pdf", "Medical report.pdf", "application/pdf", GrvKit.PdfBytes.Length, new DateTime(2026, 10, 15, 11, 0, 0));
        Grievance g = kit.Repo.AddGrievance(GrvKit.Employee, category, documents: new[] { doc });
        kit.Storage.Objects[doc.Key] = GrvKit.PdfBytes;
        return (kit, g);
    }

    [Fact]
    public async Task The_employee_download_streams_the_file_with_safe_headers_and_disposes_the_stream()
    {
        (GrvKit kit, Grievance g) = WithDocument();
        GrievancesController controller = new(kit.Employees, kit.Categories) { ControllerContext = NewContext() };

        IActionResult result = await controller.DownloadDocument(g.Id, 0, default);

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);                   // the stored validated type
        Assert.Equal("Medical report.pdf", file.FileDownloadName);           // the sanitised stored name
        Assert.False(file.EnableRangeProcessing);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("private, no-store", controller.Response.Headers["Cache-Control"]);

        (DefaultHttpContext http, byte[] body) = await ExecuteAsync(result);
        Assert.Equal(GrvKit.PdfBytes, body);
        Assert.True(kit.Storage.LastOpened!.WasDisposed);                    // the stream is released after sending
        string disposition = http.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", disposition);
        Assert.Contains("Medical report.pdf", disposition);
        Assert.False(http.Response.Headers.ContainsKey("Accept-Ranges"));
    }

    [Fact]
    public async Task A_range_request_gets_the_whole_document_not_a_partial_response()
    {
        (GrvKit kit, Grievance g) = WithDocument();
        GrievancesController controller = new(kit.Employees, kit.Categories) { ControllerContext = NewContext() };
        IActionResult result = await controller.DownloadDocument(g.Id, 0, default);

        (DefaultHttpContext http, byte[] body) = await ExecuteAsync(result, rangeHeader: "bytes=0-3");

        Assert.Equal(200, http.Response.StatusCode);
        Assert.Equal(GrvKit.PdfBytes.Length, body.Length);
        Assert.False(http.Response.Headers.ContainsKey("Content-Range"));
    }

    [Fact]
    public async Task The_admin_download_has_the_same_headers_and_disposal()
    {
        (GrvKit kit, Grievance g) = WithDocument();
        (GrievanceAdminController controller, _) = Admin(kit);

        IActionResult result = await controller.DownloadDocument(g.Id, 0, default);

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.False(file.EnableRangeProcessing);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
        await ExecuteAsync(result);
        Assert.True(kit.Storage.LastOpened!.WasDisposed);
    }

    [Fact]
    public async Task A_damaged_stored_name_can_never_reach_the_header_in_a_dangerous_form()
    {
        (GrvKit kit, Grievance g) = WithDocument();
        GrievanceDocumentRecord doc = GrievanceDocumentRules_Parse(kit.Repo.Grievances.Single().DocumentsJson).Single();
        kit.Repo.Grievances.Single().DocumentsJson = NSG.Mobile.Application.Grievances.Services.GrievanceDocumentRules.SerializeDocuments(
            new List<NSG.Mobile.Application.Grievances.Services.GrievanceDocumentRecord> { doc with { Name = "x\"; filename=\"evil.exe\r\nSet-Cookie: a=b.pdf" } });
        GrievancesController controller = new(kit.Employees, kit.Categories) { ControllerContext = NewContext() };

        FileStreamResult file = Assert.IsType<FileStreamResult>(await controller.DownloadDocument(g.Id, 0, default));

        Assert.DoesNotContain("\r", file.FileDownloadName);
        Assert.DoesNotContain("\n", file.FileDownloadName);
        Assert.DoesNotContain("\"", file.FileDownloadName);
        Assert.DoesNotContain(";", file.FileDownloadName);
    }

    private static List<NSG.Mobile.Application.Grievances.Services.GrievanceDocumentRecord> GrievanceDocumentRules_Parse(string? json)
        => NSG.Mobile.Application.Grievances.Services.GrievanceDocumentRules.ParseDocuments(json);

    // ── Attributes: policies, verbs, limits, rate limiting ──

    private static IEnumerable<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Fact]
    public void Each_controller_uses_exactly_its_existing_policy_and_no_action_opts_out()
    {
        Assert.Equal(DomainConstants.Policies.RequireMobileProfile, typeof(GrievancesController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(DomainConstants.Policies.RequireGrievanceAdmin, typeof(GrievanceAdminController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);

        foreach (Type controller in new[] { typeof(GrievancesController), typeof(GrievanceAdminController) })
        {
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
            foreach (MethodInfo action in Actions(controller))
            {
                Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
                Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());      // no per-action override of the policy
            }
        }
    }

    [Fact]
    public void Only_the_two_upload_actions_have_a_request_size_limit_and_are_rate_limited()
    {
        List<MethodInfo> all = Actions(typeof(GrievancesController)).Concat(Actions(typeof(GrievanceAdminController))).ToList();

        Assert.Equal(new[] { nameof(GrievancesController.Create), nameof(GrievanceAdminController.Resolve) },
            all.Where(m => m.GetCustomAttribute<RequestSizeLimitAttribute>() != null).Select(m => m.Name).ToArray());

        List<MethodInfo> limited = all.Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>() != null).ToList();
        Assert.Equal(new[] { nameof(GrievancesController.Create), nameof(GrievanceAdminController.Resolve) }, limited.Select(m => m.Name).ToArray());
        Assert.All(limited, m => Assert.Equal("grievance-write", m.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName));
    }

    [Fact]
    public void The_admin_status_change_is_a_PUT_and_only_the_category_uses_DELETE()
    {
        MethodInfo status = typeof(GrievanceAdminController).GetMethod(nameof(GrievanceAdminController.UpdateStatus))!;
        Assert.NotNull(status.GetCustomAttribute<HttpPutAttribute>());
        Assert.Equal("grievances/{id:long}/status", status.GetCustomAttribute<HttpPutAttribute>()!.Template);

        List<MethodInfo> deletes = Actions(typeof(GrievancesController)).Concat(Actions(typeof(GrievanceAdminController)))
            .Where(a => a.GetCustomAttribute<HttpDeleteAttribute>() != null).ToList();
        MethodInfo delete = Assert.Single(deletes);
        Assert.Equal(nameof(GrievanceAdminController.DeleteCategory), delete.Name);
        Assert.Equal("grievance-categories/{id:int}", delete.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    [Fact]
    public void The_employee_side_has_exactly_one_POST_and_no_separate_upload_endpoint()
    {
        List<MethodInfo> posts = Actions(typeof(GrievancesController)).Where(a => a.GetCustomAttribute<HttpPostAttribute>() != null).ToList();
        MethodInfo create = Assert.Single(posts);
        Assert.Equal(nameof(GrievancesController.Create), create.Name);
        Assert.Equal("grievances", create.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(new[] { "multipart/form-data" }, create.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.Null(typeof(GrievancesController).GetMethod("UploadDocument"));
    }

    [Fact]
    public void The_admin_side_offers_comment_and_forward_as_POSTs_and_category_CRUD()
    {
        string?[] templates = Actions(typeof(GrievanceAdminController))
            .SelectMany(a => a.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Select(h => h.HttpMethods.Single() + " " + h.Template)).OrderBy(t => t).ToArray();

        Assert.Contains("POST grievances/{id:long}/comments", templates);
        Assert.Contains("POST grievances/{id:long}/forward", templates);
        Assert.Contains("PUT grievances/{id:long}/status", templates);
        Assert.Contains("POST grievance-categories", templates);
        Assert.Contains("GET grievance-categories", templates);
        Assert.Contains("GET grievance-categories/{id:int}", templates);
        Assert.Contains("PUT grievance-categories/{id:int}", templates);
        Assert.Contains("DELETE grievance-categories/{id:int}", templates);
        Assert.DoesNotContain(templates, t => t!.Contains("activate"));      // PUT isActive replaces the separate (de)activate endpoints
    }

    [Fact]
    public void The_program_registers_the_grievance_write_policy_by_the_name_the_controllers_use()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NSG.Mobile.API", "Program.cs"));
        Assert.Contains("options.AddPolicy(\"grievance-write\"", program);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // ── Oversized upload filter ──

    private static ExceptionContext ExceptionContextFor(Exception exception)
        => new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>()) { Exception = exception };

    private static IExceptionFilter OversizeFilter()
    {
        MethodInfo upload = typeof(GrievancesController).GetMethod(nameof(GrievancesController.Create))!;
        object attribute = upload.GetCustomAttributes(inherit: false).Single(a => a.GetType().Name == "OversizedUploadIsBadRequestAttribute");
        return (IExceptionFilter)attribute;
    }

    [Fact]
    public void A_request_body_over_the_limit_becomes_a_400_that_names_the_limit_instead_of_a_500()
    {
        ExceptionContext context = ExceptionContextFor(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        OversizeFilter().OnException(context);

        Assert.True(context.ExceptionHandled);
        ObjectResult result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("5 MB", Assert.IsType<ProblemDetails>(result.Value).Detail);
    }

    [Fact]
    public void A_multipart_length_limit_error_is_treated_the_same_and_other_errors_are_left_alone()
    {
        ExceptionContext multipart = ExceptionContextFor(new InvalidDataException("Multipart body length limit 11534336 exceeded."));
        OversizeFilter().OnException(multipart);
        Assert.True(multipart.ExceptionHandled);

        ExceptionContext other = ExceptionContextFor(new InvalidOperationException("boom"));
        OversizeFilter().OnException(other);
        Assert.False(other.ExceptionHandled);
        Assert.Null(other.Result);

        ExceptionContext otherBadRequest = ExceptionContextFor(new BadHttpRequestException("bad", StatusCodes.Status400BadRequest));
        OversizeFilter().OnException(otherBadRequest);
        Assert.False(otherBadRequest.ExceptionHandled);
    }
}
