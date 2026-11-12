using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UniversityTodos.Models;
using UniversityTodos.Services;

namespace UniversityTodos.ViewModels;

public sealed partial class PlatformCredentialViewModel : ViewModelBase
{
    private readonly CredentialStore _store;
    private CredentialFile? _credential;

    public PlatformCredentialViewModel(string fileName)
    {
        _store = new CredentialStore(fileName);
    }

    [ObservableProperty]
    private string _accountInput = string.Empty;

    [ObservableProperty]
    private string _passwordInput = string.Empty;

    [ObservableProperty]
    private string _status = "未保存登录凭据";

    public CredentialFile? Current => _credential;
    public bool HasCredential => _credential is not null;

    public async Task LoadAsync()
    {
        _credential = await _store.LoadAsync().ConfigureAwait(true);
        if (_credential is null) return;
        AccountInput = _credential.Account;
        PasswordInput = _credential.Password;
        Status = $"凭据已加密保存（{_credential.SavedAt:MM-dd HH:mm}）";
        OnPropertyChanged(nameof(HasCredential));
    }

    public async Task<bool> SaveAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(AccountInput) || string.IsNullOrEmpty(PasswordInput))
            {
                Status = "学号和密码都不能为空";
                return false;
            }

            _credential = new CredentialFile
            {
                Account = AccountInput.Trim(),
                Password = PasswordInput,
            };
            await _store.SaveAsync(_credential).ConfigureAwait(true);
            Status = "凭据已加密保存";
            OnPropertyChanged(nameof(HasCredential));
            return true;
        }
        catch (System.Exception ex)
        {
            Status = $"保存失败：{ex.Message}";
            return false;
        }
    }

    public async Task ClearAsync()
    {
        await _store.ClearAsync().ConfigureAwait(true);
        _credential = null;
        AccountInput = string.Empty;
        PasswordInput = string.Empty;
        Status = "已清除凭据";
        OnPropertyChanged(nameof(HasCredential));
    }
}
