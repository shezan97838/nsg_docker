using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.SalarySlips;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Page-wise text extraction with UglyToad.PdfPig (managed code, no native dependency, no OCR). Pages are read one at a time and only
/// the account number is kept, so memory use does not grow with the size of the PDF.
/// </summary>
public class SalarySlipPdfExtractionService : ISalarySlipPdfExtractionService
{
    private readonly ILogger<SalarySlipPdfExtractionService> _logger;

    public SalarySlipPdfExtractionService(ILogger<SalarySlipPdfExtractionService> logger)
    {
        _logger = logger;
    }

    public Task<Result<IReadOnlyList<SalarySlipPageInfo>>> ExtractAsync(string pdfPath, int maxPages, CancellationToken cancellationToken)
        => Task.Run(() => Extract(pdfPath, maxPages, cancellationToken), cancellationToken);   // CPU-bound: keep it off the request thread

    private Result<IReadOnlyList<SalarySlipPageInfo>> Extract(string pdfPath, int maxPages, CancellationToken cancellationToken)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(pdfPath);
            if (document.NumberOfPages > maxPages)
                return Result.Failure<IReadOnlyList<SalarySlipPageInfo>>(ApiErrorCodes.ValidationFailed, $"The PDF has more than {maxPages} pages.");

            List<SalarySlipPageInfo> pages = new(document.NumberOfPages);
            for (int number = 1; number <= document.NumberOfPages; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages.Add(ReadPage(document, number));
            }

            return Result.Success<IReadOnlyList<SalarySlipPageInfo>>(pages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The uploaded salary slip PDF could not be read");
            return Result.Failure<IReadOnlyList<SalarySlipPageInfo>>(ApiErrorCodes.ValidationFailed, "The file is not a readable PDF document.");
        }
    }

    private SalarySlipPageInfo ReadPage(PdfDocument document, int number)
    {
        try
        {
            Page page = document.GetPage(number);

            // Reading-order text first; the raw content-stream text and the word list are fallbacks for layouts where the label and its
            // value are drawn as separate pieces. Only text after the "Bank A/c No" label is ever used (see BankAccountNumberParser).
            (string? account, SalarySlipPageIssue issue) = BankAccountNumberParser.Parse(ContentOrderTextExtractor.GetText(page));
            if (account == null && issue != SalarySlipPageIssue.NoText && issue != SalarySlipPageIssue.AccountNumberAmbiguous)
                (account, issue) = BankAccountNumberParser.Parse(page.Text);
            if (account == null && issue != SalarySlipPageIssue.NoText && issue != SalarySlipPageIssue.AccountNumberAmbiguous)
                (account, issue) = BankAccountNumberParser.Parse(string.Join(' ', page.GetWords().Select(w => w.Text)));

            return new SalarySlipPageInfo(number, account, issue);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One unreadable page must not sink the whole file: it is reported as unmatched.
            _logger.LogWarning(ex, "Page {Page} of the salary slip PDF could not be read", number);
            return new SalarySlipPageInfo(number, null, SalarySlipPageIssue.NoText);
        }
    }
}
