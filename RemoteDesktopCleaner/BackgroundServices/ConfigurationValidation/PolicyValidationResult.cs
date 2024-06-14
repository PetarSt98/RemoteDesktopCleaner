using System.Text.Json;

namespace RemoteDesktopCleaner.BackgroundServices
{
    public enum FailureDetail
    {
        ValidationException,
        LoginNotFound,
        ComputerNotFound,
        NoFailure
    }

    public class PolicyValidationResult
    {
        public string Message { get; set; }
        public bool Invalid { get; set; }
        public FailureDetail FailureDetail { get; set; }

        public PolicyValidationResult() { }
        public PolicyValidationResult(bool isInvalid)
        {
            Invalid = isInvalid;
        }
        public PolicyValidationResult(bool isInvalid, string message)
        {
            Invalid = isInvalid;
            Message = message;
        }

        public PolicyValidationResult(bool isInvalid, FailureDetail action)
        {
            Invalid = isInvalid;
            FailureDetail = action;
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
