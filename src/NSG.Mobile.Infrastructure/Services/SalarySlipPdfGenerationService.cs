using NSG.Mobile.Application.Interfaces.Infrastructure;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Builds the per-account PDFs with PDFsharp by importing the original page objects, so fonts, images and layout are carried over
/// unchanged (nothing is rendered). The upload is parsed once; each output is written straight to disk and released before the next.
/// </summary>
public class SalarySlipPdfGenerationService : ISalarySlipPdfGenerationService
{
    public Task<ISalarySlipPdfSource> OpenSourceAsync(string pdfPath, CancellationToken cancellationToken)
        => Task.Run<ISalarySlipPdfSource>(() =>
        {
            try
            {
                return new PdfSource(PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException("The PDF could not be opened for splitting.", ex);
            }
        }, cancellationToken);

    private sealed class PdfSource : ISalarySlipPdfSource
    {
        private readonly PdfDocument _source;

        public PdfSource(PdfDocument source) => _source = source;

        public int PageCount => _source.PageCount;

        public Task WritePagesAsync(IReadOnlyList<int> pageNumbers, string destinationPath, CancellationToken cancellationToken)
        {
            if (pageNumbers.Count == 0 || pageNumbers.Any(n => n < 1 || n > _source.PageCount))
                throw new ArgumentOutOfRangeException(nameof(pageNumbers));

            return Task.Run(() =>
            {
                using PdfDocument output = new();
                foreach (int number in pageNumbers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.AddPage(_source.Pages[number - 1]);
                }

                // Written under a temporary name and renamed without overwriting, so a failure never leaves a partial file under the final
                // name and an existing file is never replaced.
                string partial = destinationPath + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    using (FileStream stream = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        output.Save(stream, false);
                        stream.Flush(flushToDisk: true);
                    }

                    File.Move(partial, destinationPath, overwrite: false);
                }
                catch
                {
                    try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    throw;
                }
            }, cancellationToken);
        }

        public void Dispose() => _source.Dispose();
    }
}
