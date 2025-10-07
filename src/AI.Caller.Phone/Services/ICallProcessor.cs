namespace AI.Caller.Phone.Services;
public interface ICallProcessor {
    Task ProcessCallLogJob(int callLogId);
}