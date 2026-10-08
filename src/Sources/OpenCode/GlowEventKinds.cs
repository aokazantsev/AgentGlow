namespace AgentGlow.Sources.OpenCode
{
    internal static class GlowEventKinds
    {
        public const string Hello = "hello";
        public const string SessionCreated = "session.created";
        public const string SessionDeleted = "session.deleted";
        public const string Activity = "activity";
        public const string Busy = "busy";
        public const string Idle = "idle";
        public const string Retry = "retry";
        public const string Error = "error";
        public const string PermissionAsked = "permission.asked";
        public const string PermissionReplied = "permission.replied";
        public const string QuestionAsked = "question.asked";
        public const string QuestionReplied = "question.replied";
        public const string QuestionRejected = "question.rejected";
        public const string AbortedError = "MessageAbortedError";
    }
}
