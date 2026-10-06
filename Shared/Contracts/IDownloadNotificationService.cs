namespace Autonomuse.Shared.Contracts;

public interface IDownloadNotificationService
{
    void ShowCompleted(bool isAudio, int downloaded, int errors);
}
