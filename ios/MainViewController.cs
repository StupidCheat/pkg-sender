using System;
using System.Net;
using System.Net.Sockets;
using Foundation;
using UIKit;

namespace PKGSender // Asegúrate de que este sea el namespace de tu proyecto
{
    public partial class MainViewController : UIViewController
    {
        // UI Elements
        public UILabel FileNameLabel;
        private UIButton SelectFileButton;
        private UIButton StartServerButton;
        private UILabel StatusLabel;

        // Variables de estado
        public string SelectedPkgPath;
        public NSUrl SecurityScopedUrl; // Guardamos el URL para liberar la memoria después

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View.BackgroundColor = UIColor.SystemBackground;

            // 1. VITAL: Evitar que la pantalla se apague y corte la transferencia
            UIApplication.SharedApplication.IdleTimerDisabled = true;

            // 2. Configuración de la interfaz (UI)
            SetupUI();
        }

        private void SetupUI()
        {
            FileNameLabel = new UILabel
            {
                Text = "Ningún archivo seleccionado",
                TextAlignment = UITextAlignment.Center,
                Lines = 0,
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            SelectFileButton = new UIButton(UIButtonType.System);
            SelectFileButton.SetTitle("Seleccionar PKG", UIControlState.Normal);
            SelectFileButton.TranslatesAutoresizingMaskIntoConstraints = false;
            SelectFileButton.TouchUpInside += SelectFileButton_TouchUpInside;

            StartServerButton = new UIButton(UIButtonType.System);
            StartServerButton.SetTitle("Iniciar Servidor", UIControlState.Normal);
            StartServerButton.TranslatesAutoresizingMaskIntoConstraints = false;
            StartServerButton.TouchUpInside += StartServerButton_TouchUpInside;

            StatusLabel = new UILabel
            {
                Text = $"Tu IP Local: {GetLocalIPAddress()}\nEsperando acción...",
                TextAlignment = UITextAlignment.Center,
                Lines = 0,
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            var stackView = new UIStackView(new UIView[] { FileNameLabel, SelectFileButton, StartServerButton, StatusLabel })
            {
                Axis = UILayoutConstraintAxis.Vertical,
                Spacing = 20,
                Alignment = UIStackViewAlignment.Center,
                TranslatesAutoresizingMaskIntoConstraints = false
            };

            View.AddSubview(stackView);

            // Centrar en la pantalla
            stackView.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor).Active = true;
            stackView.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor).Active = true;
            stackView.WidthAnchor.ConstraintEqualTo(View.WidthAnchor, 0.9f).Active = true;
        }

        private void SelectFileButton_TouchUpInside(object sender, EventArgs e)
        {
            var allowedUTIs = new string[] { "public.item", "public.data" };
            var documentPicker = new UIDocumentPickerViewController(allowedUTIs, UIDocumentPickerMode.Open);
            
            // Asignamos el delegado que maneja el archivo
            documentPicker.Delegate = new PkgDocumentPickerDelegate(this);
            documentPicker.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;

            PresentViewController(documentPicker, true, null);
        }

        private void StartServerButton_TouchUpInside(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedPkgPath) || SecurityScopedUrl == null)
            {
                StatusLabel.Text = "Error: Selecciona un archivo primero.";
                return;
            }

            StatusLabel.Text = "Servidor iniciado. Ve a la consola e ingresa la IP.";
            
            // AQUÍ INICIAS TU SERVIDOR HTTP...
            // Tu código de servidor (HttpListener, Sockets, etc) va aquí.
            
            // IMPORTANTE:
            // Cuando el servidor termine de enviar el 100% del juego (o cuando el usuario detenga el servidor),
            // DEBES llamar a esta función para devolverle el permiso del archivo a iOS:
            // StopFileAccess(); 
        }

        public void StopFileAccess()
        {
            if (SecurityScopedUrl != null)
            {
                SecurityScopedUrl.StopAccessingSecurityScopedResource();
                SecurityScopedUrl = null;
                Console.WriteLine("Permiso del archivo liberado.");
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "No detectada";
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }

    // ==============================================================================
    // CLASE DELEGADO: Maneja lo que pasa cuando el usuario elige el archivo
    // ==============================================================================
    public class PkgDocumentPickerDelegate : UIDocumentPickerDelegate
    {
        private MainViewController _parentController;

        public PkgDocumentPickerDelegate(MainViewController parentController)
        {
            _parentController = parentController;
        }

        public override void DidPickDocumentAtUrls(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            if (urls == null || urls.Length == 0) return;

            NSUrl fileUrl = urls[0];

            // Si había un archivo anterior, lo liberamos primero
            _parentController.StopFileAccess();

            // 1. PEDIR PERMISO A IOS PARA LEER FUERA DE LA APP
            bool hasAccess = fileUrl.StartAccessingSecurityScopedResource();

            if (hasAccess)
            {
                // Guardamos la URL para poder liberarla después de enviar el archivo
                _parentController.SecurityScopedUrl = fileUrl;
                _parentController.SelectedPkgPath = fileUrl.Path;

                // 2. ACTUALIZAR LA INTERFAZ EN EL HILO PRINCIPAL
                _parentController.BeginInvokeOnMainThread(() =>
                {
                    _parentController.FileNameLabel.Text = $"Archivo listo:\n{fileUrl.LastPathComponent}";
                    Console.WriteLine($"Archivo cargado con permiso: {_parentController.SelectedPkgPath}");
                });
            }
            else
            {
                _parentController.BeginInvokeOnMainThread(() =>
                {
                    _parentController.FileNameLabel.Text = "Error: iOS denegó el acceso al archivo.";
                });
            }
        }

        public override void WasCancelled(UIDocumentPickerViewController controller)
        {
            Console.WriteLine("El usuario canceló.");
        }
    }
}
