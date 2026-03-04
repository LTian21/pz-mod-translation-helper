using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Octokit;

partial class Program
{
    static async Task<int> SyncRepository(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("��ʼͬ�����زֿ�...");

            if (!Directory.Exists(config.LocalPath) || !await MinGitHelper.IsValidRepositoryAsync(config.LocalPath))
            {
                Console.WriteLine("[����] ���زֿⲻ���ڣ�����ִ�� init ����");
                return 1;
            }

            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
            Console.WriteLine($"�����߷�֧: {translatorBranch}");

            var githubRepo = await github.Repository.Get(owner, repoName);
            string defaultBranch = githubRepo.DefaultBranch;
            Console.WriteLine($"Ĭ�Ϸ�֧: {defaultBranch}");

            var currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
            Console.WriteLine($"��ǰ��֧: {currentBranch}");

            // 0) fetch һ�Σ����������ж�/���������ڱ��λ�ȡ�� refs
            Console.WriteLine("[�� 0 �׶�] ��ȡԶ�˸���...");
            if (!await MinGitHelper.FetchAsync(config.LocalPath, config.Key, remote: "origin", force: false, prune: true))
            {
                Console.WriteLine("[����] ��ȡԶ�˸���ʧ��");
                return 1;
            }

            // 1) �Ȳ� PR��Զ�˷�֧������ʱ����Ҫ�����Ƿ������ؽ��� force push��
            Console.WriteLine("����Ƿ���ڿ��ŵ� PR...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr =>
                pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            // 2) ���Զ�˷�֧�Ƿ���ڣ����ܱ��ϲ���ɾ�����򱻹���Ա������
            var remoteTranslatorExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch, checkRemote: true);
            if (!remoteTranslatorExists)
            {
                // ������Զ����ɾ����֧
                if (existingPR != null)
                {
                    // �п��� PR����Զ�˷�֧�����ڣ���ͨ����ζ�� PR ���쳣/�رա�Ȩ������� refs ��һ�¡�
                    // ��ʱäĿ�ؽ���ǿ�ƿ��ܵ��� PR ָ�����/��ʷ��ʧ�����ֱ��ʧ�ܲ���ʾ�˹�������
                    Console.WriteLine($"[����] ���ֿ��� PR (#{existingPR.Number})����Զ�˷�֧ origin/{translatorBranch} �����ڡ�\n" +
                                      "���� GitHub �ϼ��� PR ״̬/��֧�Ƿ�ɾ������������Ϊ�����ƻ� PR��sync ����ֹ��");
                    return 1;
                }

                Console.WriteLine($"[��ʾ] δ�ҵ�Զ�̷�֧ origin/{translatorBranch}�������ѱ�ɾ��/������������ {defaultBranch} �ؽ�������...");

                // 2.1) ����Ĭ�Ϸ�֧��Ϊ�ؽ�����
                if (!await MinGitHelper.EnsureLocalBranchAtRemoteAsync(config.LocalPath, config.Key, "origin", defaultBranch, fetchFirst: false))
                {
                    Console.WriteLine("[����] ǿ��ͬ��Ĭ�Ϸ�֧ʧ��");
                    return 1;
                }

                // 2.2) ������ػ�����ͬ����֧������ɾ�����ؽ������Ȿ����ʷ����Ⱦ���ؽ���֧��
                //      ע�⣺����ǰ�����ڸ÷�֧���޷�ɾ������ʱ�����Ѿ��л�/���뵽�� defaultBranch��
                bool localTranslatorExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch, checkRemote: false);
                if (localTranslatorExists)
                {
                    if (!string.Equals(currentBranch, defaultBranch, StringComparison.OrdinalIgnoreCase))
                    {
                        // EnsureLocalBranchAtRemoteAsync ���������� defaultBranch����������һ�α���
                        currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
                    }

                    if (!string.Equals(currentBranch, translatorBranch, StringComparison.OrdinalIgnoreCase))
                    {
                        var del = await ExecuteGitCommandAsync($"branch -D \"{translatorBranch}\"", config.LocalPath);
                        if (del.exitCode == 0)
                        {
                            Console.WriteLine($"[��ʾ] ��ɾ�����ز�����֧: {translatorBranch}");
                        }
                        else
                        {
                            // ɾ��ʧ�ܲ�һ�����������ܷ�֧������/����/����ԭ�򣩣����� checkout -B �Կ��ܳɹ�
                            Console.WriteLine($"[����] ɾ�����ط�֧ʧ�ܣ������������ؽ���: {del.error.Trim()}");
                        }
                    }
                }

                // 2.3) ����/�ؽ������֧�� force push �ָ�Զ��
                if (!await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: true))
                {
                    Console.WriteLine($"[����] �޷�����/�л�����֧ {translatorBranch}");
                    return 1;
                }

                if (!await MinGitHelper.PushHeadToRemoteBranchAsync(config.LocalPath, config.Key, "origin", translatorBranch, force: true))
                {
                    Console.WriteLine("[����] �ؽ������ͷ����߷�֧ʧ��");
                    return 1;
                }

                currentBranch = translatorBranch;
                remoteTranslatorExists = true;
            }

            // 3) Զ�˷�֧���ڣ���֤���ع�����λ�ڷ����֧
            if (!string.Equals(currentBranch, translatorBranch, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"�л��������֧: {translatorBranch}");
                if (!await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: false))
                {
                    Console.WriteLine($"[����] �޷��л�����֧ {translatorBranch}");
                    return 1;
                }
                currentBranch = translatorBranch;
            }

            // 4) ǿ��ͬ��ǰ������Ƿ���δ�ύ�޸�
            bool hasLocalChanges = await MinGitHelper.HasChangesAsync(config.LocalPath);
            if (hasLocalChanges)
            {
                Console.WriteLine("[����] ��⵽���ش���δ�ύ�޸ģ���������ִ��Ӳ���ò�������Щ�޸ġ�");
                Console.WriteLine("[��ʾ] ���豣�������ȱ���/�����޸��ļ���������ִ�� sync��");
            }

            // 5) ��֧�������
            if (existingPR != null)
            {
                // ���� 3/5�������ύ + Զ�˿��� rebase/force push
                // �� PR��ֻ���뵽Զ�˷�֧�����ص�Ĭ�Ϸ�֧���ߣ�
                Console.WriteLine($"[ͬ��] reset --hard �� origin/{translatorBranch}");
                if (!await MinGitHelper.ResetToRemoteAsync(config.LocalPath, "origin", translatorBranch))
                {
                    Console.WriteLine("[����] ���õ�Զ�̷����߷�֧ʧ��");
                    return 1;
                }

                Console.WriteLine("[�ɹ�] ��ͬ����Զ�˷����߷�֧������ PR ��������");
                Console.WriteLine("[�ɹ�] ͬ�����!");
                return 0;
            }

            // �� PR�������ֻ�ǡ��մ�����֧δ�ύ���������Ѻϲ��ҷ�֧���������ؽ�����
            // ����Ĳ����ǣ��÷����֧ʼ�մ����� defaultBranch ��������֤�ɾ����ߡ�
            // �������� remoteTranslatorExists ��ȷ�����ڣ���˿���ֱ���� defaultBranch ���ò� force push��

            if (!await MinGitHelper.EnsureLocalBranchAtRemoteAsync(config.LocalPath, config.Key, "origin", defaultBranch, fetchFirst: false))
            {
                Console.WriteLine("[����] ǿ��ͬ��Ĭ�Ϸ�֧ʧ��");
                return 1;
            }

            Console.WriteLine($"[ͬ��] �� {translatorBranch} ���õ�Ĭ�Ϸ�֧ {defaultBranch} ��ǿ�����͵�Զ��...");
            if (!await MinGitHelper.PushHeadToRemoteBranchAsync(config.LocalPath, config.Key, "origin", translatorBranch, force: true))
            {
                Console.WriteLine("[����] ǿ�����ͷ����߷�֧ʧ��");
                return 1;
            }

            if (!await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: false))
            {
                Console.WriteLine($"[����] �޷��л��ط����֧ {translatorBranch}");
                return 1;
            }

            Console.WriteLine($"[ͬ��] reset --hard �� origin/{translatorBranch}");
            if (!await MinGitHelper.ResetToRemoteAsync(config.LocalPath, "origin", translatorBranch))
            {
                Console.WriteLine("[����] ���õ�Զ�̷����߷�֧ʧ��");
                return 1;
            }

            Console.WriteLine("[�ɹ�] ͬ�����!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[����] ͬ��ʧ��: {ex.Message}");
            Console.WriteLine($"[��ջ����] {ex.StackTrace}");
            return 1;
        }
    }

    // Helper method to execute git commands (used for specific operations not in MinGitHelper)
    private static async Task<(int exitCode, string output, string error)> ExecuteGitCommandAsync(
        string arguments,
        string workingDirectory)
    {
        var gitPath = MinGitHelper.GetGitExecutablePath();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = gitPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Apply proxy settings
        var proxyUrl = ProxyHelper.GetHttpProxyUrl();

        // Force OpenSSL and ignore system config
        startInfo.EnvironmentVariables["GIT_SSL_BACKEND"] = "openssl";
        startInfo.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.EnvironmentVariables["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        if (!string.IsNullOrEmpty(proxyUrl))
        {
            startInfo.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
            startInfo.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
        }
        else
        {
            startInfo.EnvironmentVariables["HTTP_PROXY"] = "";
            startInfo.EnvironmentVariables["HTTPS_PROXY"] = "";
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}
