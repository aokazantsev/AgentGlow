namespace AgentGlow
{
    internal sealed class ThreadInfo
    {
        public string Source;
        public string Project;
        public string IdTail;
        public GlowStatus Status;

        public string Label
        {
            get
            {
                string tail = string.IsNullOrEmpty(IdTail) ? "" : ", …" + IdTail;
                return Project + " (" + Source + tail + ") — " + StatusCatalog.DisplayName(Status);
            }
        }
    }
}
