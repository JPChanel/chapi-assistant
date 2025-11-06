using AI.Clients;
using Chapi.Helper.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Chapi.Views
{
    /// <summary>
    /// Lógica de interacción para SqlGeneratorView.xaml
    /// </summary>
    public partial class SqlGeneratorView : Window
    {
        public SqlGeneratorView()
        {
            InitializeComponent();
            CboDbType.SelectedIndex = 0;
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validar entradas
            if (string.IsNullOrWhiteSpace(TxtSpName.Text) ||
                string.IsNullOrWhiteSpace(TxtNetParams.Text) ||
                CboDbType.SelectedItem == null)
            {
                TxtSqlOutput.Text = "Error: Por favor, complete el tipo de BD, el nombre del SP/Función y los parámetros.";
                return;
            }

            string dbType = (CboDbType.SelectedItem as ComboBoxItem).Content.ToString();
            string spName = TxtSpName.Text.Trim();
            string netParams = TxtNetParams.Text.Trim();

            LoadingOverlay.Visibility = Visibility.Visible;
            TxtSqlOutput.Text = "Generando SQL con IA, por favor espera...";

            try
            {
                // 2. Crear el Prompt
                var prompt = GetPrompt.GenerateSqlCall(spName, dbType, netParams);

                // 3. Llamar a la IA
                var sqlResult = await AIClient.SendPromptAsync(prompt);

                // 4. Mostrar resultado
                if (string.IsNullOrWhiteSpace(sqlResult))
                {
                    TxtSqlOutput.Text = "Error: La IA no devolvió ningún resultado.";
                }
                else
                {
                    TxtSqlOutput.Text = sqlResult;
                }
            }
            catch (System.Exception ex)
            {
                TxtSqlOutput.Text = $"Error fatal al contactar la IA: {ex.Message}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtSqlOutput.Text))
            {
                Clipboard.SetText(TxtSqlOutput.Text);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
