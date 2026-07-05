const DB_NAME = 'ecosim_batch_reports';
const DB_VERSION = 1;
const STORE = 'reports';
const CAMPAIGN_STORE = 'campaigns';

export class BatchReportStore
{
  constructor()
  {
    this._db = null;
    this._opening = null;
  }

  _open()
  {
    if (this._db) return Promise.resolve(this._db);
    if (this._opening) return this._opening;
    this._opening = new Promise((resolve, reject) =>
    {
      const req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = () =>
      {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE))
        {
          const store = db.createObjectStore(STORE, { keyPath: 'runId' });
          store.createIndex('startedAt', 'startedAt');
          store.createIndex('outcome', 'outcome');
        }
        if (!db.objectStoreNames.contains(CAMPAIGN_STORE))
        {
          const store = db.createObjectStore(CAMPAIGN_STORE, { keyPath: 'campaignId' });
          store.createIndex('startedAt', 'startedAt');
        }
      };
      req.onsuccess = () =>
      {
        this._db = req.result;
        resolve(this._db);
      };
      req.onerror = () => reject(req.error || new Error('Failed to open batch report DB'));
    }).finally(() =>
    {
      this._opening = null;
    });
    return this._opening;
  }

  async saveReport(report)
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._db.transaction([STORE], 'readwrite');
      tx.objectStore(STORE).put(report);
      tx.oncomplete = () => resolve(report);
      tx.onerror = () => reject(tx.error);
    });
  }

  async saveCampaign(campaign)
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._db.transaction([CAMPAIGN_STORE], 'readwrite');
      tx.objectStore(CAMPAIGN_STORE).put({ ...campaign, startedAt: campaign.startedAt || new Date().toISOString() });
      tx.oncomplete = () => resolve(campaign);
      tx.onerror = () => reject(tx.error);
    });
  }

  async listReports(limit = 50)
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._db.transaction([STORE], 'readonly');
      const req = tx.objectStore(STORE).index('startedAt').openCursor(null, 'prev');
      const out = [];
      req.onsuccess = () =>
      {
        const cursor = req.result;
        if (!cursor || out.length >= limit)
        {
          resolve(out);
          return;
        }
        out.push(cursor.value);
        cursor.continue();
      };
      req.onerror = () => reject(req.error);
    });
  }

  async listCampaigns(limit = 20)
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._db.transaction([CAMPAIGN_STORE], 'readonly');
      const req = tx.objectStore(CAMPAIGN_STORE).index('startedAt').openCursor(null, 'prev');
      const out = [];
      req.onsuccess = () =>
      {
        const cursor = req.result;
        if (!cursor || out.length >= limit)
        {
          resolve(out);
          return;
        }
        out.push(cursor.value);
        cursor.continue();
      };
      req.onerror = () => reject(req.error);
    });
  }

  async getReport(runId)
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._db.transaction([STORE], 'readonly');
      const req = tx.objectStore(STORE).get(runId);
      req.onsuccess = () => resolve(req.result || null);
      req.onerror = () => reject(req.error);
    });
  }

  async postToServer(report)
  {
    const runId = report.runId || report.campaignId;
    if (!runId) return false;
    try
    {
      const res = await fetch('/api/batch-reports', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(report),
      });
      return res.ok;
    }
    catch (e)
    {
      return false;
    }
  }
}

export const batchReportStore = new BatchReportStore();
