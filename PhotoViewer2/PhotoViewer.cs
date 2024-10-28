using Microsoft.VisualBasic;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VBIO = Microsoft.VisualBasic.FileIO;

namespace Cincinnatus
{
	public partial class PhotoViewer : Form
	{
		private enum InterpMode
		{
			None, Low, High, Smart
		}

		// ---------------------------------------------------------------------------------------

		private Image Image = null;
		private string _currentFile = null;

		private PointF ImageOrigin = PointF.Empty;
		private float ImageScale = 1.0f;
		private bool ImageFitted = false;

		private InterpMode Interp = InterpMode.Smart;

		// ---------------------------------------------------------------------------------------

		public PhotoViewer()
		{
			InitializeComponent();

			// generate context menu
			ContextMenuStrip contextMenuStrip = new ContextMenuStrip();

            // open image button

            var mnuOpen = new ToolStripMenuItem("Open", null, (o, e) => PromptOpenImage());
			mnuOpen.ShortcutKeys = Keys.Control | Keys.O;
			mnuOpen.ShortcutKeyDisplayString = "Ctrl+O";
            contextMenuStrip.Items.Add(mnuOpen);            

            var mnuCut = new ToolStripMenuItem("Copy", null, (o, e) => CopyImageFile());
            mnuCut.ShortcutKeys = Keys.Control | Keys.C;
            mnuCut.ShortcutKeyDisplayString = "Ctrl+C";
            contextMenuStrip.Items.Add(mnuCut);

            var mnuDelete = new ToolStripMenuItem("Delete", null, (o, e) => DeleteImageFile());
            mnuDelete.ShortcutKeys = Keys.Control | Keys.Delete;
            mnuDelete.ShortcutKeyDisplayString = "Ctrl+Del";
            contextMenuStrip.Items.Add(mnuDelete);

			var mnuRename = new ToolStripMenuItem("Rename", null, (o, e) => RenameImageFile());
			mnuRename.ShortcutKeys = Keys.Control | Keys.R;
			mnuRename.ShortcutKeyDisplayString = "Ctrl+R";
			contextMenuStrip.Items.Add(mnuRename);


			// create the drop-down for the InterpolationMode values and link them to context menu
			ToolStripMenuItem interpModes = new ToolStripMenuItem("Interpolation", null);
			contextMenuStrip.Items.Add(interpModes);

			foreach (InterpMode mode in Enum.GetValues(typeof(InterpMode)))
			{
				ToolStripMenuItem item = new ToolStripMenuItem(Utility.TransformCammelCase(mode.ToString()), null);
				item.Checked = Interp == mode;
				item.Click += (o, e) =>
				{
					if (Interp == mode) return; // switching to same mode is no-op
					Interp = mode;

					foreach (ToolStripMenuItem other in interpModes.DropDownItems) other.Checked = false;
					item.Checked = true;

					Invalidate();
				};

				interpModes.DropDownItems.Add(item);
			}

			// create drop-down for background color values
			ToolStripMenuItem colorModes = new ToolStripMenuItem("Background", null);
			contextMenuStrip.Items.Add(colorModes);

			foreach (KeyValuePair<Color, string> entry in new List<KeyValuePair<Color, string>>()
				{
					new KeyValuePair<Color, string>(Color.Black, "Black"),
					new KeyValuePair<Color, string>(Color.White, "White"),
				})
			{
				ToolStripMenuItem item = new ToolStripMenuItem(entry.Value, null);
				item.Checked = BackColor == entry.Key;
				item.Click += (o, e) =>
				{
					BackColor = entry.Key;

					foreach (ToolStripMenuItem other in colorModes.DropDownItems)
						other.Checked = false;
					item.Checked = true;
				};

				colorModes.DropDownItems.Add(item);
			}

			// allow user to reset view (e.g. zoom out too far and lose image)
			contextMenuStrip.Items.Add(new ToolStripMenuItem("Reset View", null, (o, e) => ResetViewFit()));

			// allow user to set zoom to actual image size
			contextMenuStrip.Items.Add(new ToolStripMenuItem("Actual Size", null, (o, e) => ResetViewTrueSize()));

			// launch another instance of the viewer (not tied to this one)
			contextMenuStrip.Items.Add(new ToolStripMenuItem("New Window", null, (o, e) => Process.Start(new ProcessStartInfo(Application.ExecutablePath))));

			//associate image next formats to this app
			contextMenuStrip.Items.Add(new ToolStripMenuItem("Associate!", null, (o, e) => AssociateImageTypes()));

			ContextMenuStrip = contextMenuStrip;

			// initialize null image
			SetImage(null);
		}

        private void RenameImageFile()
        {
			// TODO: File is locked by another process :\

			if (_currentFile == null)
				return;

            System.IO.FileInfo fi = new System.IO.FileInfo(_currentFile);

			if (!fi.Exists)
				return;
			            
            var path = Path.GetDirectoryName(_currentFile);
			var fname = Path.GetFileName(_currentFile);
			var ext = Path.GetExtension(fname);
			
            if (InputBox.Show("Rename file", "New file name:", ref fname) == DialogResult.Cancel)
				return;

			var newExt = Path.GetExtension(fname);
			if (string.IsNullOrEmpty(newExt))
				fname += ext;

            var newName = Path.Combine(path, fname);

            try
			{				
				{
					fi.MoveTo(newName);
					SetImage(newName);
				}
            }
			catch(Exception ex)
			{
				MessageBox.Show($"Could not rename file to {newName} \n {ex.Message}");
			}
        }

        private void DeleteImageFile()
        {
            if (_currentFile == null)
                return;

			var prev = PrevImage();
			var next = NextImage();

            if (prev == _currentFile)
                prev = null;

            if (next == _currentFile)
				next = null;            

            try
			{
				// Check if the next exists before attempting to delete it
				if (File.Exists(_currentFile))
				{
					try
					{
						// Use Visual Basic's FileSystem to delete the file
						VBIO.FileSystem.DeleteFile(_currentFile,
							VBIO.UIOption.OnlyErrorDialogs,
							VBIO.RecycleOption.SendToRecycleBin);
					}
					catch (Exception ex)
					{
						MessageBox.Show($"Error deleting file: {ex.Message}");
					}
				}
            }
            catch (IOException ioEx)
            {
                MessageBox.Show($"IO Exception: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}");
            }

			// try to set prev/next image
			if (next != null)
                SetImage(next);
            else if (prev != null)
                SetImage(prev); 
			else
				SetImage(null);
        }

		private void CopyImageFile()
		{
			if (_currentFile == null)
				return;

			try
			{
				// Prepare the DataObject with the file path
				var dataObject = new DataObject();
				dataObject.SetData(DataFormats.FileDrop, new[] { _currentFile });								

				// Set the data to the clipboard with a custom format indicating cut
				Clipboard.SetDataObject(dataObject, true);

				this.Text = "READY FOR COPY: " + _currentFile;

            }
			catch (Exception ex)
			{
				MessageBox.Show($"Error: {ex.Message}");
			}
		}

        public string GetFullAppPath()
		{
            return System.Reflection.Assembly.GetEntryAssembly().Location;
        }

        public void AssociateImageTypes()
		{
			string appPath = GetFullAppPath();

            var exts = string.Join(", ", SupportedExtensions);

            DialogResult dialogResult = MessageBox.Show(
				$"Do you want to associate extensions: \n\n{exts}\n\n to this viewer?", 
				"Confirm", MessageBoxButtons.YesNo);

			if (dialogResult == DialogResult.No)
				return;

			const string appFriendlyName = "Cincinnatus2";

            try
            {
                foreach (string ext in SupportedExtensions)
                {
                    // Register the next type
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\*.{ext}"))
                    {
                        key.SetValue("", appFriendlyName); 
                    }

                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\.{ext}"))
                    {
                        key.SetValue("", appFriendlyName);
                    }

                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + appFriendlyName))
                    {
                        key.SetValue("", appFriendlyName);
                        key.CreateSubKey("shell").CreateSubKey("open")
							.CreateSubKey("command").SetValue("", $"\"{appPath}\" \"%1\"");
                    }
                }

                MessageBox.Show("Successfully associated file types  with application.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

		// ---------------------------------------------------------------------------------------

		private const float SmartInterpThresh = 8.0f;
		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			if (Image != null)
			{
				float imageWidth = Image.Width * ImageScale;
				float imageHeight = Image.Height * ImageScale;

				switch (Interp)
				{
					case InterpMode.None: e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor; break;
					case InterpMode.Low: e.Graphics.InterpolationMode = InterpolationMode.Low; break;
					case InterpMode.High: e.Graphics.InterpolationMode = InterpolationMode.High; break;
					case InterpMode.Smart: e.Graphics.InterpolationMode = ImageScale >= SmartInterpThresh ? InterpolationMode.NearestNeighbor : InterpolationMode.High; break;
				}

				e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

				e.Graphics.DrawImage(Image, new RectangleF(ImageOrigin.X, ImageOrigin.Y, imageWidth, imageHeight));
			}
		}

		private const int DragInterval = 13;
		protected async override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);

			// if there's not an image, there's no need to scroll
			if (Image == null) return;

			PointF origStart = ImageOrigin;
			Point start = e.Location, last = e.Location;
			while (MouseButtons == MouseButtons.Left)
			{
				await Task.Delay(DragInterval);

				// ignore frames where the mouse didn't move
				Point now = PointToClient(MousePosition);
				if (now == last) continue;
				last = now;

				// set new origin and redraw canvas
				ImageOrigin = new PointF(origStart.X + now.X - start.X, origStart.Y + now.Y - start.Y);
				ImageFitted = false;
				Invalidate();
			}
		}

		private const float ZoomMod = 1.25f;
		private const float MaxZoom = 1000f;
		private const float MinZoom = 0.001f;

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);

			// if there's not an image, there's no need to zoom
			if (Image == null) return;

			// vector equations to determine how to reposition origin

			// act = orig1 + pos * scale1
			// act = orig2 + pos * scale2
			// 0 = orig1 - orig2 + pos * (scale1 - scale2)
			// orig2 = orig1 + pos * (scale1 - scale2)

			// pos = (act - orig1) / scale1

			// orig2 = orig1 + (scale1 - scale2) * (act - orig1) / scale1
			// orig2 = orig1 + (1 - scale2 / scale1) * (act - orig1)

			// scale after operation
			float newScale = (e.Delta > 0 ? ImageScale * ZoomMod : ImageScale / ZoomMod).Clamp(MinZoom, MaxZoom);

			// pre-compute this for efficiency
			float scaleFactor = 1f - newScale / ImageScale;

			ImageOrigin = new PointF(
				ImageOrigin.X + scaleFactor * (e.Location.X - ImageOrigin.X),
				ImageOrigin.Y + scaleFactor * (e.Location.Y - ImageOrigin.Y));
			ImageScale = newScale;
			ImageFitted = false;

			// redraw canvas
			Invalidate();
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			// if image is fitted to the viewport, maintain that state
			if (ImageFitted) ResetViewFit();
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);

			ResetViewFit(); // in case image is set prior to displaying the window (e.g. from command line, including "open with")
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);

			switch (e.KeyCode)
			{
			case Keys.Right:
				if ((ModifierKeys & Keys.Control) != Keys.None) FlipHorizontal();
				else
				{
					string next = NextImage();
					if (next != null) SetImage(next);
				}
				break;
			case Keys.Left:
				if ((ModifierKeys & Keys.Control) != Keys.None) FlipHorizontal();
				else
				{
					string prev = PrevImage();
					if (prev != null) SetImage(prev);
				}
				break;

			case Keys.Up:
				if ((ModifierKeys & Keys.Control) != Keys.None) FlipVertical();
				else Clockwise90();
				break;
			case Keys.Down:
				if ((ModifierKeys & Keys.Control) != Keys.None) FlipVertical();
				else Anticlockwise90();
				break;
			}

            if (_currentFile == null) return;

			if (e.KeyCode == Keys.E && e.Control)
			{
                System.Diagnostics.ProcessStartInfo procInfo = new System.Diagnostics.ProcessStartInfo();
                procInfo.FileName = ("mspaint.exe");
                procInfo.Arguments = $"\"{_currentFile}\"";
                System.Diagnostics.Process.Start(procInfo);

				Watch(Path.GetDirectoryName(_currentFile));
            }
		}

		// ---------------------------------------------------------------------------------------

		/// <summary>
		/// centers the display image and zooms the image to fit in the display window and redraws the display.
		/// </summary>
		private void ResetViewFit()
		{
			if (Image != null)
			{
				float imgWidth = Image.Width, imgHeight = Image.Height;

				ImageScale = Math.Min(
					ClientRectangle.Width / imgWidth,
					ClientRectangle.Height / imgHeight);
				ImageOrigin = new PointF(
					(ClientRectangle.Width - imgWidth * ImageScale) / 2f,
					(ClientRectangle.Height - imgHeight * ImageScale) / 2f);
				ImageFitted = true;
			}

			// redraw canvas regardless of if there was an image (in case image was set to null from non-null)
			Invalidate();
		}
		/// <summary>
		/// centers the display image and zooms to the actual image size and redraws the display.
		/// </summary>
		private void ResetViewTrueSize()
		{
			if (Image != null)
			{
				float imgWidth = Image.Width, imgHeight = Image.Height;

				ImageScale = 1;
				ImageOrigin = new PointF(
					(ClientRectangle.Width - imgWidth) / 2f,
					(ClientRectangle.Height - imgHeight) / 2f);
				ImageFitted = false;
			}

			// redraw canvas regardless of if there was an image (in case image was set to null from non-null)
			Invalidate();
		}

		/// <summary>
		/// Prompts the user to select an image then displays it if possible
		/// </summary>
		private void PromptOpenImage()
		{
            
			//dlg.Filter = "Image files (*.txt)|*.txt|All files (*.*)|*.*";

			var sep = string.Empty;
			OpenFileDialog dlg = new OpenFileDialog();
			// Image files (*.jpg;*.png;*.bmp)|*.jpg;*.png;*.bmp|All files(*.*)|*.*

			var exts = string.Join(";", SupportedExtensions.Select(x => "*." + x));
            dlg.Filter = $"Image files ({exts})|{exts}|All files(*.*)|*.*";
            dlg.InitialDirectory = Path.GetFullPath(GetFullAppPath());

            using (dlg)
			{
				dlg.Title = "Open Image";
				if (dlg.ShowDialog() == DialogResult.OK) 
					SetImage(dlg.FileName);
			}
		}

		/// <summary>
		/// Rotates the image 90 degrees clockwise and redraws the canvas
		/// </summary>
		private void Clockwise90()
		{
			if (Image == null) return;
			Image.RotateFlip(RotateFlipType.Rotate90FlipNone);

			// if image is fitted to screen, maintain that condition
			if (ImageFitted) ResetViewFit();
			// otherwise rotate image about the center of the display
			else
			{
				PointF center = new PointF(ClientRectangle.Width / 2f, ClientRectangle.Height / 2f);
				PointF off = new PointF(ImageOrigin.X - center.X, ImageOrigin.Y - center.Y);

				float nx = center.X - off.Y - Image.Width * ImageScale;
				float ny = center.Y + off.X;

				ImageOrigin = new PointF(nx, ny);
				Invalidate();
			}
		}
		/// <summary>
		/// Rotates the image 90 degrees clockwise and redraws the canvas
		/// </summary>
		private void Anticlockwise90()
		{
			if (Image == null) return;
			Image.RotateFlip(RotateFlipType.Rotate270FlipNone);

			// if image is fitted to screen, maintain that condition
			if (ImageFitted) ResetViewFit();
			// otherwise rotate image about the center of the display
			else
			{
				PointF center = new PointF(ClientRectangle.Width / 2f, ClientRectangle.Height / 2f);
				PointF off = new PointF(ImageOrigin.X - center.X, ImageOrigin.Y - center.Y);

				float nx = center.X + off.Y;
				float ny = center.Y - off.X - Image.Height * ImageScale;

				ImageOrigin = new PointF(nx, ny);
				Invalidate();
			}
		}

		/// <summary>
		/// Flips the image vertically and redraws the canvas
		/// </summary>
		private void FlipVertical()
		{
			if (Image == null) return;
			Image.RotateFlip(RotateFlipType.RotateNoneFlipY);

			// if image is fitted to screen, we can maintain that condition with no-op
			if (ImageFitted) Invalidate();
			// otherwise flip image about the center of the display
			else
			{
				float center = ClientRectangle.Height / 2f;
				float off = ImageOrigin.Y - center;

				ImageOrigin.Y = center - off - Image.Height * ImageScale;
				Invalidate();
			}
		}
		/// <summary>
		/// Flips the image horizontally and redraws the canvas
		/// </summary>
		private void FlipHorizontal()
		{
			if (Image == null) return;
			Image.RotateFlip(RotateFlipType.RotateNoneFlipX);

			// if image is fitted to screen, we can maintain that condition with no-op
			if (ImageFitted) Invalidate();
			// otherwise flip image about the center of the display
			else
			{
				float center = ClientRectangle.Width / 2f;
				float off = ImageOrigin.X - center;

				ImageOrigin.X = center - off - Image.Width * ImageScale;
				Invalidate();
			}
		}

		// ---------------------------------------------------------------------------------------

		private static readonly HashSet<string> SupportedExtensions = new HashSet<string>()
		{
			"bmp", "gif", "jpg", "jpeg", "jpe", "jfif", "png", "tif", "tiff"
		};
		private IEnumerable<string> GetImages()
		{
			return Directory.EnumerateFiles(Path.GetDirectoryName(_currentFile), "*.*",  
				System.IO.SearchOption.TopDirectoryOnly)
				.Where(f => SupportedExtensions.Contains(f.Substring(f.LastIndexOf('.') + 1).ToLower()))
				.OrderBy(f => Path.GetFileName(f));
		}

		/// <summary>
		/// gets the fileName to the next image in the display cycle, or null on failure
		/// </summary>
		private string NextImage()
		{
			// handle no loaded image fileName gracefully
			if (_currentFile == null) return null;
			// get the loaded image name
			string loaded_name = Path.GetFileName(_currentFile);

			// get an iterator for all image files
			IEnumerator<string> iter = GetImages().GetEnumerator();
			// if it's empty, return null
			if (!iter.MoveNext()) return null;

			// get the first fname from the iterator
			string first = iter.Current;

			// iterate through all the files (do-while since iter currently points at a valid position)
			do
			{
				// if the fileToDelete next is the loaded next
				if (Path.GetFileName(iter.Current) == loaded_name)
				{
					// return next if present, otherwise first (wrap around)
					return iter.MoveNext() ? iter.Current : first;
				}
			}
			while (iter.MoveNext());

			// the only way this happens is if the loaded next wasn't in the next set - return null to indicate failure
			return null;
		}
		/// <summary>
		/// gets the fileName to the previous image in the display cycle, or null on failure
		/// </summary>
		private string PrevImage()
		{
			// handle no loaded image fileName gracefully
			if (_currentFile == null) return null;
			// get the loaded image name
			string loaded_name = Path.GetFileName(_currentFile);

			// keep track of the previous fname - initially null
			string prev = null;

			// iterate through all the files
			for (IEnumerator<string> iter = GetImages().GetEnumerator(); iter.MoveNext(); prev = iter.Current)
			{
				// if the fileToDelete next is the loaded next
				if (Path.GetFileName(iter.Current) == loaded_name)
				{
					// if we have a file, use that
					if (prev != null) return prev;

					// otherwise we need to return the last next (wrap around)
					do prev = iter.Current; while (iter.MoveNext());
					return prev;
				}
			}

			// the only way this happens is if the loaded next wasn't in the next set - return null to indicate failure
			return null;
		}       
        
        public bool SetImage(string fileName)
		{
			// if we currently have an image, dispose it
			if (Image != null) Image.Dispose();

			if (fileName == null)
			{
				Image = null;
				_currentFile = null;
			}
			else
			{
				Image img = null;

				try {

                    // old code:
                    //img = Image.FromFile(fileName); 
                    //https://stackoverflow.com/questions/6576341/open-image-from-next-then-release-lock
                    //https://stackoverflow.com/questions/4803935/free-next-locked-by-new-bitmapfilepath/8701748#8701748
                    using (var bmpTemp = new Bitmap(fileName))
                    {
                        img = new Bitmap(bmpTemp);
                    }                    

                }
				catch (FileNotFoundException)
				{
					MessageBox.Show($"File {fileName} does not exist", "Failed to Open Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}
				catch (Exception e)
				{
					MessageBox.Show($"Failed to read image {fileName}", "Failed to Open Image\n" + e.Message , MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				Image = img;
				_currentFile = fileName;
			}

			Text = fileName != null ? $"{Application.ProductName} - {fileName}" : Application.ProductName;

			ResetViewFit();
			return true;
		}        

        private void PhotoViewer_Load(object sender, EventArgs e)
        {   
            this.WindowState = FormWindowState.Maximized;
        }

        private void Watch(string path)
        {
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = path;
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Filter = "*.*";
            watcher.Changed += new FileSystemEventHandler(OnChanged);
            watcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
			SetImage(_currentFile);
        }
    }
}
