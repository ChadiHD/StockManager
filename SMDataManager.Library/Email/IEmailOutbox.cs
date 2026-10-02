namespace SMDataManager.Library.Email
{
    /// <summary>
    /// Queues a message from C#, for the triggers that have no procedure to queue it for them.
    /// </summary>
    /// <remarks>
    /// The call sites' seam since T6. <c>IEmailSender</c> is now the transport behind the
    /// dispatcher; a call site that sends through it directly skips the retry, the
    /// dead-letter and the alert, and waits on a relay inside its own request.
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
