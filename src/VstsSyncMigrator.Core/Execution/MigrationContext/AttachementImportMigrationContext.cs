using Microsoft.TeamFoundation.WorkItemTracking.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using VstsSyncMigrator.Engine.Configuration.Processing;

namespace VstsSyncMigrator.Engine
{
    public class AttachementImportMigrationContext : AttachementMigrationContextBase
    {
        public override string Name
        {
            get
            {
                return "AttachementImportMigrationContext";
            }
        }
        public AttachementImportMigrationContext(MigrationEngine me, AttachementImportMigrationConfig config) : base(me, config)
        {

        }

        internal override void InternalExecute()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            //////////////////////////////////////////////////
            WorkItemStoreContext targetStore = new WorkItemStoreContext(me.Target, WorkItemStoreFlags.BypassRules);
            Project destProject = targetStore.GetProject();

            Trace.WriteLine(string.Format("Found target project as {0}", destProject.Name));

            // Read files from sub-directories as well.
            List<string> files = System.IO.Directory.EnumerateFiles(exportPath, "*",SearchOption.AllDirectories).ToList<string>();
            WorkItem targetWI = null;
            int current = files.Count;
            int failures = 0;
            int skipped = 0;
            foreach (string file in files)
            {
                // We will re-try for a max of 10 attempts before erroring out.
                const int maxRetryCount = 10;
                var reTryCount = 0;
                while (reTryCount < maxRetryCount)
                {
                    reTryCount++;
                    try
                    {
                        // Get the file name to check in TFS/VSTS for existing attachments.
                        var targetFileName = System.IO.Path.GetFileName(file);
                        
                        // Get Folder Name from the file name and base folder path.
                        // We are doing this because we have work item level folders.
                        var completeDirectory = Path.GetDirectoryName(file);
                        var directory = completeDirectory.Replace(exportPath, string.Empty).Remove('\\').Trim();

                        // Get the Reflector Id from the directory.
                        var reflectedId = directory.Replace('+', ':').Replace("--", "/");

                        targetWI = targetStore.FindReflectedWorkItemByReflectedWorkItemId(reflectedId,
                            me.ReflectedWorkItemIdFieldName);
                        if (targetWI != null)
                        {
                            Trace.WriteLine(string.Format("{0} of {1} - Import {2} to {3}", current, files.Count,
                                targetFileName, targetWI.Id));
                            var attachments = targetWI.Attachments.Cast<Attachment>();
                            var attachment = attachments.Where(a => a.Name == targetFileName).FirstOrDefault();
                            if (attachment == null)
                            {
                                Attachment a = new Attachment(file);
                                targetWI.Attachments.Add(a);
                                targetWI.Save();
                            }
                            else
                            {
                                Trace.WriteLine(string.Format(" [SKIP] WorkItem {0} already contains attachment {1}",
                                    targetWI.Id, targetFileName));
                                skipped++;
                            }
                        }
                        else
                        {
                            Trace.WriteLine(string.Format("{0} of {1} - Skipping {2} to {3}", current, files.Count,
                                targetFileName, 0));
                            skipped++;
                        }

                        System.IO.File.Delete(file);
                        break;
                    }
                    catch (FileAttachmentException ex)
                    {
                        // Probably due to attachment being over size limit
                        Trace.WriteLine($" Attempt {reTryCount} of {maxRetryCount} :{ex.Message}");
                        failures++;
                    }
                    catch (Exception ex)
                    {
                        // Any other exception.
                        Trace.WriteLine($" Attempt {reTryCount} of {maxRetryCount} :{ex.Message}");
                        failures++;
                    }
                }

                current--;
            }
            //////////////////////////////////////////////////
            stopwatch.Stop();
            Trace.WriteLine(string.Format(@"IMPORT DONE in {0:%h} hours {0:%m} minutes {0:s\:fff} seconds - {4} Files, {1} Files imported, {2} Failures, {3} Skipped", stopwatch.Elapsed, (files.Count - failures - skipped), failures, skipped, files.Count));
        }

    }
}