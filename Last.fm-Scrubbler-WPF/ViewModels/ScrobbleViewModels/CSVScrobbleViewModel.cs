﻿using IF.Lastfm.Core.Objects;
using Last.fm_Scrubbler_WPF.Models;
using Last.fm_Scrubbler_WPF.Properties;
using Last.fm_Scrubbler_WPF.Views;
using Last.fm_Scrubbler_WPF.Views.ScrobbleViews;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Last.fm_Scrubbler_WPF.ViewModels
{
  /// <summary>
  /// Scrobble mode.
  /// </summary>
  public enum CSVScrobbleMode
  {
    /// <summary>
    /// Scrobble the tracks based on the parsed timestamp.
    /// </summary>
    Normal,

    /// <summary>
    /// Set the timestamp by setting <see cref="CSVScrobbleViewModel.FinishingTime"/>.
    /// </summary>
    ImportMode
  }

  /// <summary>
  /// ViewModel for the <see cref="Views.CSVScrobbleView"/>.
  /// </summary>
  class CSVScrobbleViewModel : ScrobbleViewModelBase
  {
    #region Properties

    /// <summary>
    /// Different formats to try in case TryParse fails.
    /// </summary>
    public static string[] Formats = new string[] { "M/dd/yyyy h:mm" };

    /// <summary>
    /// The path to the csv file.
    /// </summary>
    public string CSVFilePath
    {
      get { return _csvFilePath; }
      private set
      {
        _csvFilePath = value;
        NotifyOfPropertyChange(() => CSVFilePath);
        NotifyOfPropertyChange(() => CanParse);
      }
    }
    private string _csvFilePath;

    /// <summary>
    /// The parsed scrobbles from the csv file.
    /// </summary>
    public ObservableCollection<ParsedCSVScrobbleViewModel> Scrobbles
    {
      get { return _scrobbles; }
      private set
      {
        _scrobbles = value;
        NotifyOfPropertyChange(() => Scrobbles);
      }
    }
    private ObservableCollection<ParsedCSVScrobbleViewModel> _scrobbles;

    /// <summary>
    /// The selected <see cref="CSVScrobbleMode"/>.
    /// </summary>
    public CSVScrobbleMode ScrobbleMode
    {
      get { return _scrobbleMode; }
      set
      {
        _scrobbleMode = value;

        if (Scrobbles.Count > 0)
        {
          if (MessageBox.Show("Do you want to switch the Scrobble Mode? The CSV file will be parsed again!", "Change Scrobble Mode", MessageBoxButtons.YesNo) == DialogResult.Yes)
            ParseCSVFile();
        }

        NotifyOfPropertyChange(() => ScrobbleMode);
        NotifyOfPropertyChange(() => ShowImportModeSettings);
      }
    }
    private CSVScrobbleMode _scrobbleMode;

    /// <summary>
    /// The time the last track played.
    /// </summary>
    public DateTime FinishingTime
    {
      get { return _finishingTime; }
      set
      {
        _finishingTime = value;
        NotifyOfPropertyChange(() => FinishingTime);
      }
    }
    private DateTime _finishingTime;

    /// <summary>
    /// Duration between scrobbles in seconds.
    /// </summary>
    public int Duration
    {
      get { return _duration; }
      set
      {
        _duration = value;
        NotifyOfPropertyChange(() => Duration);
      }
    }
    private int _duration;

    /// <summary>
    /// Gets if the Scrobble button is enabled on the UI.
    /// </summary>
    public override bool CanScrobble
    {
      get { return MainViewModel.Client.Auth.Authenticated && Scrobbles.Any(i => i.ToScrobble) && EnableControls; }
    }

    /// <summary>
    /// Gets if the preview button is enabled.
    /// </summary>
    public override bool CanPreview
    {
      get { return Scrobbles.Any(i => i.ToScrobble) && EnableControls; }
    }

    /// <summary>
    /// Gets if the "Select All" button is enabled.
    /// </summary>
    public bool CanSelectAll
    {
      get { return !Scrobbles.All(i => i.ToScrobble) && EnableControls; }
    }

    /// <summary>
    /// Gets if the "Select None" button is enabled.
    /// </summary>
    public bool CanSelectNone
    {
      get { return Scrobbles.Any(i => i.ToScrobble) && EnableControls; }
    }

    /// <summary>
    /// Gets if the "Parse" button is enabled.
    /// </summary>
    public bool CanParse
    {
      get { return CSVFilePath != null && CSVFilePath != string.Empty; }
    }

    /// <summary>
    /// Gets if the import mode settings should be visible on the UI.
    /// </summary>
    public bool ShowImportModeSettings
    {
      get { return ScrobbleMode == CSVScrobbleMode.ImportMode; }
    }

    /// <summary>
    /// Gets/sets if certain controls on the UI should be enabled.
    /// </summary>
    public override bool EnableControls
    {
      get { return _enableControls; }
      protected set
      {
        _enableControls = value;
        NotifyOfPropertyChange(() => EnableControls);
        NotifyOfPropertyChange(() => CanScrobble);
        NotifyOfPropertyChange(() => CanPreview);
        NotifyOfPropertyChange(() => CanSelectAll);
        NotifyOfPropertyChange(() => CanSelectNone);
      }
    }

    #endregion Properties

    /// <summary>
    /// Dispatcher used to invoke from another thread.
    /// </summary>
    private Dispatcher _dispatcher;

    /// <summary>
    /// Constructor.
    /// </summary>
    public CSVScrobbleViewModel()
    {
      Scrobbles = new ObservableCollection<ParsedCSVScrobbleViewModel>();
      Duration = 1;
      FinishingTime = DateTime.Now;
      _dispatcher = Dispatcher.CurrentDispatcher;
      ScrobbleMode = CSVScrobbleMode.ImportMode;
    }

    /// <summary>
    /// Shows a dialog to open a csv file.
    /// </summary>
    public void LoadCSVFileDialog()
    {
      OpenFileDialog ofd = new OpenFileDialog()
      {
        Filter = "CSV Files|*.csv"
      };
      if (ofd.ShowDialog() == DialogResult.OK)
        CSVFilePath = ofd.FileName;
    }

    /// <summary>
    /// Loads and parses a csv file.
    /// </summary>
    /// <returns>Task.</returns>
    public async Task ParseCSVFile()
    {
      try
      {
        EnableControls = false;
        OnStatusUpdated("Reading CSV file...");
        Scrobbles.Clear();

        TextFieldParser parser = new TextFieldParser(CSVFilePath)
        {
          HasFieldsEnclosedInQuotes = true
        };
        parser.SetDelimiters(",");

        string[] fields = new string[0];
        List<string> errors = new List<string>();

        await Task.Run(() =>
        {
          while (!parser.EndOfData)
          {
            try
            {
              fields = parser.ReadFields();

              DateTime date = DateTime.Now;
              string dateString = fields[Settings.Default.TimestampFieldIndex];

              // check for 'now playing'
              if (dateString == "" && ScrobbleMode == CSVScrobbleMode.Normal)
                continue;

              if (DateTime.TryParse(dateString, out date))
              {

              }
              else
              {
                bool parsed = false;
                // try different formats until succeeded
                foreach (string format in Formats)
                {
                  parsed = DateTime.TryParseExact(dateString, format, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
                  if (parsed)
                    break;
                }

                if (!parsed && ScrobbleMode == CSVScrobbleMode.Normal)
                  throw new Exception("Timestamp could not be parsed!");
              }

              CSVScrobble parsedScrobble = new CSVScrobble(fields[Settings.Default.ArtistFieldIndex], fields[Settings.Default.AlbumFieldIndex],
                                                           fields[Settings.Default.TrackFieldIndex], date.AddSeconds(1));
              ParsedCSVScrobbleViewModel vm = new ParsedCSVScrobbleViewModel(parsedScrobble, ScrobbleMode);
              vm.ToScrobbleChanged += ToScrobbleChanged;
              _dispatcher.Invoke(() => Scrobbles.Add(vm));
            }
            catch (Exception ex)
            {
              string errorString = "CSV line number: " + parser.LineNumber + ",";
              foreach (string s in fields)
              {
                errorString += s + ",";
              }

              errorString += ex.Message;
              errors.Add(errorString);
            }
          }
        });

        if (errors.Count == 0)
          OnStatusUpdated("Successfully parsed CSV file. Parsed " + Scrobbles.Count + " rows");
        else
        {
          OnStatusUpdated("Partially parsed CSV file. " + errors.Count + " rows could not be parsed");
          if (MessageBox.Show("Some rows could not be parsed. Do you want to save a text file with the rows that could not be parsed?", "Error parsing rows", MessageBoxButtons.YesNo) == DialogResult.Yes)
          {
            SaveFileDialog sfd = new SaveFileDialog()
            {
              Filter = "Text Files|*.txt"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
              File.WriteAllLines(sfd.FileName, errors.ToArray());
          }
        }
      }
      catch (Exception ex)
      {
        Scrobbles.Clear();
        OnStatusUpdated("Error parsing CSV file: " + ex.Message);
      }
      finally
      {
        EnableControls = true;
      }
    }

    /// <summary>
    /// Notifies the UI that "ToScrobble" has changed.
    /// </summary>
    /// <param name="sender">Ignored.</param>
    /// <param name="e">Ignored.</param>
    private void ToScrobbleChanged(object sender, EventArgs e)
    {
      NotifyOfPropertyChange(() => CanScrobble);
      NotifyOfPropertyChange(() => CanPreview);
      NotifyOfPropertyChange(() => CanSelectAll);
      NotifyOfPropertyChange(() => CanSelectNone);
    }

    /// <summary>
    /// Scrobbles the selected scrobbles.
    /// </summary>
    /// <returns>Task.</returns>
    public override async Task Scrobble()
    {
      EnableControls = false;
      OnStatusUpdated("Trying to scrobble selected tracks...");

      var response = await MainViewModel.Scrobbler.ScrobbleAsync(CreateScrobbles());
      if (response.Success)
        OnStatusUpdated("Successfully scrobbled!");
      else
        OnStatusUpdated("Error while scrobbling!");

      EnableControls = true;
    }

    /// <summary>
    /// Previews the tracks that will be scrobbled.
    /// </summary>
    public override void Preview()
    {
      ScrobblePreviewView spv = new ScrobblePreviewView(new ScrobblePreviewViewModel(CreateScrobbles()));
      spv.ShowDialog();
    }

    /// <summary>
    /// Create a list with tracks that will be scrobbled.
    /// </summary>
    /// <returns>List with scrobbles.</returns>
    private List<Scrobble> CreateScrobbles()
    {
      List<Scrobble> scrobbles = new List<Scrobble>();

      if (ScrobbleMode == CSVScrobbleMode.Normal)
      {
        foreach (var vm in Scrobbles.Where(i => i.ToScrobble))
        {
          scrobbles.Add(new Scrobble(vm.ParsedScrobble.Artist, vm.ParsedScrobble.Album, vm.ParsedScrobble.Track, vm.ParsedScrobble.DateTime));
        }
      }
      else if (ScrobbleMode == CSVScrobbleMode.ImportMode)
      {
        DateTime time = FinishingTime;
        foreach (var vm in Scrobbles.Where(i => i.ToScrobble))
        {
          scrobbles.Add(new Scrobble(vm.ParsedScrobble.Artist, vm.ParsedScrobble.Album, vm.ParsedScrobble.Track, time));
          time = time.Subtract(TimeSpan.FromSeconds(Duration));
        }
      }

      return scrobbles;
    }

    /// <summary>
    /// Opens the <see cref="ConfigureCSVParserView"/>
    /// </summary>
    public void OpenCSVParserSettings()
    {
      ConfigureCSVParserView view = new ConfigureCSVParserView();
      view.ShowDialog();
    }

    /// <summary>
    /// Marks all scrobbles as "ToScrobble".
    /// </summary>
    public void SelectAll()
    {
      foreach (var vm in Scrobbles.Where(i => i.IsEnabled))
      {
        vm.ToScrobble = true;
      }
    }

    /// <summary>
    /// Marks all scrobbles as not "ToScrobble".
    /// </summary>
    public void SelectNone()
    {
      foreach (var vm in Scrobbles.Where(i => i.IsEnabled))
      {
        vm.ToScrobble = false;
      }
    }
  }
}