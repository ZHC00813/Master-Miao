namespace Blankedge.SWHoleTools.Model
{
    public sealed class OperationResult<T>
    {
        private OperationResult(bool success, T value, string error)
        {
            Success = success;
            Value = value;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public T Value { get; }
        public string Error { get; }

        public static OperationResult<T> Ok(T value) => new OperationResult<T>(true, value, string.Empty);
        public static OperationResult<T> Fail(string error) => new OperationResult<T>(false, default(T), error);
    }
}
