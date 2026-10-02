namespace SMDataManager.Library.Email
{
    /// <summary>
    /// Queues a message from C#, for the triggers that have no procedure to queue it for them.
    /// </summary>
    /// <remarks>
    /// The call sites' seam since T6. <c>IEmailSender</c> is now the transport behind the
    /// dispatcher and nothing else calls it; a call site that sent through it directly would
    /// skip the retry, the dead-letter and the alert, and wait on a relay inside its own
    /// request. <c>TransportCallerTests</c> holds that.
    /// </remarks>
    public interface IEmailOutbox
    {
        /// <summary>
        /// Records that a message is owed. False, and nothing queued, when there is no address.
        /// </summary>
        bool Enqueue<TPayload>(
            int siteId, string to, string toName, EmailTemplate<TPayload> template, TPayload payload);
    }
}
