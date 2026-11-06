namespace Toetsr.Services;

public enum ToastKind { Success, Error, Info }

public record Toast(Guid Id, string Message, ToastKind Kind);

/// <summary>Korte meldingen rechtsonder ("Opgeslagen", "Verwijderd", ...). Eén instantie per gebruikerssessie.</summary>
public class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public void Show(string message, ToastKind kind = ToastKind.Success, int milliseconds = 3500)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind);
        _toasts.Add(toast);
        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, milliseconds);
    }

    public void Success(string message) => Show(message);
    public void Error(string message) => Show(message, ToastKind.Error, 6000);
    public void Info(string message) => Show(message, ToastKind.Info);

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke();
    }

    private async Task DismissLaterAsync(Guid id, int ms)
    {
        await Task.Delay(ms);
        Dismiss(id);
    }
}
