using TMPro;
using UnityEngine;

public class AuthManager : MonoBehaviour
{
    [Header("--- PANELS ---")]
    public GameObject panelAuth;
    public GameObject subPanelLogin;
    public GameObject subPanelRegister;
    public GameObject subPanelForgot;

    [Header("--- INPUT FIELDS ---")]
    public TMP_InputField loginUser;
    public TMP_InputField loginPass;
    public TMP_InputField regUser;
    public TMP_InputField regPass;
    public TMP_InputField regConfirmPass;
    public TMP_InputField forgotInpUser;
    public TextMeshProUGUI txtResultPassword;

    [Header("--- REFERENCES ---")]
    public AuthUIHandler authUIHandler;

    void Start() { OpenLogin(); }

    public void OpenLogin() { SetAllSubPanelsFalse(); subPanelLogin.SetActive(true); }
    public void OpenRegister() { SetAllSubPanelsFalse(); subPanelRegister.SetActive(true); }
    public void OpenForgot() { SetAllSubPanelsFalse(); subPanelForgot.SetActive(true); txtResultPassword.text = ""; }

    private void SetAllSubPanelsFalse()
    {
        subPanelLogin.SetActive(false);
        subPanelRegister.SetActive(false);
        subPanelForgot.SetActive(false);
    }
    // New helper: instantly treat button press as a successful login and switch to main panel.
    // Wire your login Button to call this if you want immediate local-login behavior.
    public void HandleLoginInstant()
    {
        // Require both username and password to be non-empty
        if (string.IsNullOrWhiteSpace(loginUser.text) || string.IsNullOrWhiteSpace(loginPass.text))
        {
            authUIHandler?.uiStatus?.SetStatus("Vui lòng nhập tên và mật khẩu.", Color.yellow);
            return;
        }

        string username = loginUser.text.Trim();
        string password = loginPass.text; // don't Trim() password to preserve intentional leading/trailing (if any)

        // Prefer to perform a real server LOGIN so the server can persist "ĐĂNG NHẬP" to NhatKyHoatDong.
        if (authUIHandler != null && authUIHandler.networkManager != null)
        {
            authUIHandler.uiStatus?.SetStatus("Đang đăng nhập...", Color.white);
            authUIHandler.Login(username, password);
            return;
        }

        // Fallback: local-only login (no server log). Keep the old local behavior.
        authUIHandler?.uiStatus?.SetStatus("Đăng nhập thành công (local)!", Color.green);

        if (authUIHandler != null && authUIHandler.mainMenuManager != null)
        {
            authUIHandler.mainMenuManager.LoginSuccess(username);
        }
        else
        {
            // Fallback: hide auth panel locally if available
            if (panelAuth != null) panelAuth.SetActive(false);
            Debug.LogWarning("AuthUIHandler.mainMenuManager not assigned. Closed auth panel locally.");
        }
    }

    public void HandleRegister()
    {
        string user = regUser?.text?.Trim() ?? "";
        string pass = regPass?.text ?? "";
        string confirm = regConfirmPass?.text ?? "";

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            authUIHandler?.uiStatus?.SetStatus("Vui lòng nhập tên và mật khẩu.", Color.yellow);
            return;
        }

        if (pass != confirm)
        {
            authUIHandler.uiStatus?.SetStatus("Mật khẩu không khớp!", Color.red);
            return;
        }

        authUIHandler.uiStatus?.SetStatus("Đang đăng ký...", Color.white);
        authUIHandler.Register(user, pass);
    }

    public void HandleConfirmUser()
    {
        string user = forgotInpUser?.text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(user))
        {
            authUIHandler?.uiStatus?.SetStatus("Vui lòng nhập tên.", Color.yellow);
            return;
        }

        authUIHandler.uiStatus?.SetStatus("Đang kiểm tra tài khoản...", Color.white);
        authUIHandler.ResetPassword(user);
    }
}