namespace SMDesktopUI.Services
{
    public interface ILabelPrintService
    {
        LabelPrintResult Print(string jobName, string labelContent);
    }

    public record LabelPrintResult(bool WasPrinted, bool WasCancelled, string Message);
}
