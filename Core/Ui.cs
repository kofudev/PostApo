using System;
using System.Collections.Generic;
using System.Linq;
using Life.Network;
using Life.UI;

namespace PostApo.Core
{
    /// <summary>
    /// Fabriques de panels bases exclusivement sur <see cref="UIPanel"/>, l'interface native du jeu.
    ///
    /// Deux contraintes dictent ce fichier :
    ///  - un panel de texte n'affiche qu'une dizaine de lignes : au-dela le texte passe sous le
    ///    bouton. Toute la mise en page passe donc par <see cref="Paginate"/> ;
    ///  - <c>AddTabLine(nom, prix, iconId, action)</c> combine a <c>PanelType.TabPrice</c> permet
    ///    d'afficher l'icone d'un item a cote de chaque ligne : on s'en sert partout ou une ligne
    ///    correspond a un objet du jeu.
    /// </summary>
    public static class Ui
    {
        public const string ColorOk = "#7BC96F";
        public const string ColorBad = "#E06C75";
        public const string ColorAccent = "#C96F4A";
        public const string ColorDim = "#9A9A9A";

        /// <summary>Budget de caracteres tenant dans un panel sans deborder (retours a la ligne compris).</summary>
        private const int PageBudget = 300;

        public static string Ok(string text) { return "<color=" + ColorOk + ">" + text + "</color>"; }
        public static string Bad(string text) { return "<color=" + ColorBad + ">" + text + "</color>"; }
        public static string Accent(string text) { return "<color=" + ColorAccent + ">" + text + "</color>"; }
        public static string Dim(string text) { return "<color=" + ColorDim + ">" + text + "</color>"; }

        /// <summary>Une ligne de menu. <see cref="Icon"/> = id d'item dont l'icone sera affichee.</summary>
        public sealed class MenuEntry
        {
            public string Label;
            public Action Action;
            public int Icon;
            public string Price = "";

            public MenuEntry(string label, Action action)
            {
                Label = label;
                Action = action;
            }

            public MenuEntry(string label, int icon, string price, Action action)
            {
                Label = label;
                Icon = icon;
                Price = price ?? "";
                Action = action;
            }
        }

        // ------------------------------------------------------------------ pagination

        /// <summary>
        /// Decoupe un texte en pages tenant dans un panel. Les coupures se font sur les lignes
        /// vides quand c'est possible, pour ne pas casser un paragraphe en deux.
        /// </summary>
        public static List<string> Paginate(IEnumerable<string> lines, int budget)
        {
            var pages = new List<string>();
            var current = new List<string>();
            var length = 0;

            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                var line = raw ?? string.Empty;

                // Une ligne longue occupe plusieurs lignes a l'ecran : on compte les caracteres,
                // pas les sauts de ligne.
                var cost = Math.Max(line.Length, 1) + 24;

                if (length + cost > budget && current.Count > 0)
                {
                    pages.Add(string.Join("\n", current.ToArray()).TrimEnd());
                    current.Clear();
                    length = 0;
                }

                current.Add(line);
                length += cost;
            }

            if (current.Count > 0)
            {
                pages.Add(string.Join("\n", current.ToArray()).TrimEnd());
            }

            if (pages.Count == 0) { pages.Add(string.Empty); }
            return pages;
        }

        public static List<string> Paginate(string body)
        {
            return Paginate((body ?? string.Empty).Split('\n'), PageBudget);
        }

        // ------------------------------------------------------------------ panels

        /// <summary>Panel de texte simple avec un unique bouton d'action.</summary>
        public static void Text(Player player, string title, string body, string buttonLabel, Action onValidate)
        {
            try
            {
                if (player == null) { return; }

                var panel = new UIPanel(Utils.Sanitize(title, 46), UIPanel.PanelType.Text);
                panel.SetText(body ?? string.Empty);
                panel.AddButton(buttonLabel ?? "Continuer", ui =>
                {
                    player.ClosePanel(ui);
                    Invoke(onValidate);
                });

                player.ShowPanelUI(panel);
            }
            catch (Exception ex)
            {
                Utils.Warn("panel texte '" + title + "' : " + ex.Message);
                Invoke(onValidate);
            }
        }

        /// <summary>
        /// Texte long affiche page par page. Le bouton enchaine les pages puis appelle
        /// <paramref name="onDone"/> — utilise partout ou le contenu peut deborder.
        /// </summary>
        public static void LongText(Player player, string title, string body, string lastButtonLabel, Action onDone)
        {
            var pages = Paginate(body);
            ShowPage(player, title, pages, 0, lastButtonLabel, onDone);
        }

        private static void ShowPage(Player player, string title, List<string> pages, int index,
                                     string lastButtonLabel, Action onDone)
        {
            if (index >= pages.Count)
            {
                Invoke(onDone);
                return;
            }

            var isLast = index == pages.Count - 1;
            var heading = pages.Count > 1
                ? title + "  (" + (index + 1) + "/" + pages.Count + ")"
                : title;

            Text(player, heading, pages[index],
                isLast ? (lastButtonLabel ?? "Fermer") : "Suivant",
                () =>
                {
                    if (isLast) { Invoke(onDone); }
                    else { ShowPage(player, title, pages, index + 1, lastButtonLabel, onDone); }
                });
        }

        /// <summary>Panel d'information : pagine automatiquement, aucun effet de bord.</summary>
        public static void Info(Player player, string title, string body)
        {
            LongText(player, title, body, "Fermer", null);
        }

        /// <summary>
        /// Liste d'options. Le corps est tronque a une page : un menu doit rester lisible,
        /// les details vont dans un panel d'info dedie.
        /// </summary>
        public static void Menu(Player player, string title, string body, IEnumerable<MenuEntry> entries,
                                string cancelLabel, Action onCancel)
        {
            try
            {
                if (player == null) { return; }

                var list = (entries ?? Enumerable.Empty<MenuEntry>())
                    .Where(e => e != null && !string.IsNullOrEmpty(e.Label))
                    .ToList();

                // TabPrice permet les icones ; on ne l'active que si une ligne en demande une.
                var withIcons = list.Any(e => e.Icon > 0);
                var panel = new UIPanel(Utils.Sanitize(title, 46),
                    withIcons ? UIPanel.PanelType.TabPrice : UIPanel.PanelType.Tab);

                var pages = Paginate(body);
                panel.SetText(pages.Count > 0 ? pages[0] : string.Empty);

                foreach (var entry in list)
                {
                    var captured = entry;
                    Action<UIPanel> action = ui =>
                    {
                        player.ClosePanel(ui);
                        Invoke(captured.Action);
                    };

                    if (withIcons)
                    {
                        // Icon porte un item id cote appelant ; le panel attend un index d'icone.
                        panel.AddTabLine(captured.Label, captured.Price ?? "", Utils.IconOf(captured.Icon), action);
                    }
                    else
                    {
                        panel.AddTabLine(captured.Label, action);
                    }
                }

                if (list.Count == 0)
                {
                    panel.AddTabLine(Dim("Aucune option disponible"), ui => player.ClosePanel(ui));
                }

                panel.AddButton("Valider", ui => ui.SelectTab());
                if (onCancel != null || !string.IsNullOrEmpty(cancelLabel))
                {
                    panel.AddButton(cancelLabel ?? "Fermer", ui =>
                    {
                        player.ClosePanel(ui);
                        Invoke(onCancel);
                    });
                }

                player.ShowPanelUI(panel);
            }
            catch (Exception ex)
            {
                Utils.Warn("panel menu '" + title + "' : " + ex.Message);
            }
        }

        /// <summary>Confirmation binaire.</summary>
        public static void Confirm(Player player, string title, string body, string yesLabel, string noLabel,
                                   Action onYes, Action onNo)
        {
            var entries = new List<MenuEntry>
            {
                new MenuEntry(yesLabel ?? "Oui", onYes),
                new MenuEntry(noLabel ?? "Non", onNo),
            };

            Menu(player, title, body, entries, null, null);
        }

        /// <summary>Saisie texte libre.</summary>
        public static void Input(Player player, string title, string body, string placeholder,
                                 Action<string> onValidate, Action onCancel)
        {
            try
            {
                if (player == null) { return; }

                var panel = new UIPanel(Utils.Sanitize(title, 46), UIPanel.PanelType.Input);
                panel.SetText(body ?? string.Empty);
                panel.inputPlaceholder = placeholder ?? string.Empty;

                panel.AddButton("Valider", ui =>
                {
                    var value = panel.inputText == null ? string.Empty : panel.inputText.Trim();
                    player.ClosePanel(ui);
                    if (onValidate != null)
                    {
                        try { onValidate(value); }
                        catch (Exception ex) { Utils.Warn("input '" + title + "' : " + ex.Message); }
                    }
                });

                panel.AddButton("Annuler", ui =>
                {
                    player.ClosePanel(ui);
                    Invoke(onCancel);
                });

                player.ShowPanelUI(panel);
            }
            catch (Exception ex)
            {
                Utils.Warn("panel input '" + title + "' : " + ex.Message);
                Invoke(onCancel);
            }
        }

        private static void Invoke(Action action)
        {
            if (action == null) { return; }
            try { action(); }
            catch (Exception ex) { Utils.Warn("action de panel : " + ex.Message); }
        }
    }
}
