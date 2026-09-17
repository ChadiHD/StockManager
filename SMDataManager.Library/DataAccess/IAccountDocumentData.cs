using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// Metadata for customer-uploaded supporting documents. See <see cref="IAccountData"/>
    /// for why siteId is mandatory and last on every method.
    /// </summary>
    public interface IAccountDocumentData
    {
        /// <summary>
        /// Documents on an account, without their stored names. A list that carried the
        /// store key would hand every caller the means to fetch every file.
        /// </summary>
        List<AccountDocumentModel> GetByAccount(int accountId, int siteId);

        /// <summary>
        /// One document including its stored name, for the endpoint that streams it back.
        /// Null for an id belonging to another store — which the endpoint should render as
        /// 404, not 403, since 403 confirms the document exists.
        /// </summary>
        /// <remarks>
        /// This resolves the row; it does not finish authorising it. The caller still has to
        /// establish that the requester may see this particular account: a store admin may
        /// see any of their store's, a contact only their own account's.
        /// </remarks>
        AccountDocumentModel GetById(int id, int siteId);

        /// <summary>
        /// Records a file already committed to the document store.
        /// </summary>
        /// <remarks>
        /// Store the bytes first, then call this. A row with no file behind it is a broken
        /// download; a file with no row is an orphan nobody serves, which is the cheaper
        /// failure of the two.
        /// </remarks>
        AccountDocumentModel Insert(AccountDocumentModel document, int siteId);

        void SetStatus(int id, string status, int siteId);
    }
}
