using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharedMemoryObject
{
	
	/// <summary>
	/// Stato della memoria condivisa
	/// </summary>
	public enum SharedMemoryStatus
	{
		NotCreated,
		Opened,
		Created
	}

	/// <summary>
	/// Memoria condivisa con un Memory Mapped File.
	/// Usa MemoryMappedFile.OpenExisting() presente solo su sistemi Windows 
	/// </summary>
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	public sealed class SharedObject<T> where T : struct	// Non-nullable value type con operatore new()
	{
		public delegate void ActionRef(ref T x);

		string _mmpfName;					// Nome della condivisione
		long _mmpfSize;						// Dimensione
		string _mutexName;					// Nome del mutex
		bool _mutexCreated;					// Flag
		SharedMemoryStatus _status;			// Stato della memoria condivisa

		MemoryMappedFile? _mmpf;			// Memory mapped file
		Mutex? _mmpfMutex;					// Mutex

		bool _isOk;							// Flag 
		StringBuilder _sbErrMsg;			// Messaggi di errore

		/***************************************/

		/// <summary>
		/// Status (readonly)
		/// </summary>
		public SharedMemoryStatus Status
		{
			get { return _status; }
		}

		/// <summary>
		/// Messaggio di errore
		/// </summary>
		public string ErrorMessage
		{
			get {return _sbErrMsg.ToString();}
		}

		/***************************************/

		/// <summary>
		/// CTOR
		/// </summary>
		/// <param name="mmpfName">Nome del memory mapped file</param>
		/// <param name="mmpfSize">Dimensione del memory mapped file</param>
		/// <param name="mutexName">Nome del mutex (se null: usa il nome del memory mapped file)</param>
		public SharedObject(string mmpfName, string? mutexName)
		{
			_mmpfName = mmpfName;
			_mmpfSize = Marshal.SizeOf(typeof(T));		// Dimensioni in byte di un oggetto generico, non gestito

			if(mutexName != null)
			{
				_mutexName = mutexName;
			}
			else
			{
				_mutexName = _mmpfName;
			}
			_mutexCreated = false;
			_isOk = true;
			_status = SharedMemoryStatus.NotCreated;
			_sbErrMsg = new StringBuilder();
		}

		/// <summary>
		/// Azzera i messaggi di errore
		/// </summary>
		public void ClearErrorMessage()
		{
			_sbErrMsg.Clear();
		}	

		/// <summary>
		/// Crea il memory mapped file ed il mutex
		/// </summary>
		/// <returns>false se error</returns>
		public bool Create()
		{
			bool ok = true;
			if(_status == SharedMemoryStatus.NotCreated)
			{
				try
				{
					_mmpf = MemoryMappedFile.CreateNew(	_mmpfName,
														_mmpfSize,
														MemoryMappedFileAccess.ReadWrite,
														MemoryMappedFileOptions.None,			// Not delayed allocation
														HandleInheritability.Inheritable
														);	
					_mmpfMutex = new Mutex (	false,					// Initially not owned
												_mutexName,
												out _mutexCreated
												);
					if(!_mutexCreated)
					{
						_mmpf.Dispose();
						_mmpf = null;
						ok = false;
						_sbErrMsg.AppendLine ("Mutex not created");
					}
				}
				catch (Exception ex)
				{
					ok = false;
					_sbErrMsg.AppendLine(ex.Message);
				}
			}
			else
			{
				ok = false;
				_sbErrMsg.AppendLine($"Create() failed: status is already {_status.ToString()}");
			}

			if(ok)
			{
				_status = SharedMemoryStatus.Created;
				_sbErrMsg.Clear();
			}
			else
			{
				_isOk = false;
			}
			return ok;
		}

		/// <summary>
		/// Apre il memory mapped file ed il mutex
		/// </summary>
		/// <returns></returns>
		public bool Open()
		{
			bool ok = true;
			if(_status == SharedMemoryStatus.NotCreated)
			{
				try
				{
					_mmpf = MemoryMappedFile.OpenExisting(_mmpfName);	// Only Windows
					_mmpfMutex = Mutex.OpenExisting(_mutexName);		// Initially not owned. Non usa Mutex.TryOpenExisting(...) per catturare le eccezioni
					
				}	
				catch (Exception ex)
				{
					ok = false;
					_sbErrMsg.AppendLine(ex.Message);
				}
			}
			else
			{
				ok = false;
				_sbErrMsg.AppendLine($"Open() failed: status is already {_status.ToString()}");
			}

			if(ok)
			{
				_status = SharedMemoryStatus.Opened;
				_sbErrMsg.Clear();
			}
			else
			{
				_isOk = false;
			}
			return ok;
		}
		
		/// <summary>
		/// Legge una copia dell'oggetto condiviso
		/// </summary>
		/// <returns></returns>
		public T? Read()
		{
			T? ret = null;
			if( ((_status == SharedMemoryStatus.Created)||(_status == SharedMemoryStatus.Opened)) && (_mmpfMutex != null) && (_mmpf != null))
			{
				try
				{
					T _ret;
					_mmpfMutex.WaitOne();			// Attende che il mutex sia disponibile e poi lo blocca
					using(MemoryMappedViewAccessor _mmva =  _mmpf.CreateViewAccessor())
					{
						_mmva.Read(0, out _ret);
						ret = _ret;
					}
				}
				catch (Exception ex)
				{
					_sbErrMsg.AppendLine(ex.Message);
				}
				finally
				{
					_mmpfMutex.ReleaseMutex();		// Rilascia il mutex
				}
			}
			return ret;
		}

		/// <summary>
		/// Copia l'argomento nell'oggetto condiviso
		/// </summary>
		/// <param name="val"></param>
		/// <returns></returns>
		public bool Write(T val)
		{
			bool ok = false;
			if( ((_status == SharedMemoryStatus.Created)||(_status == SharedMemoryStatus.Opened)) && (_mmpfMutex != null) && (_mmpf != null))
			{
				try
				{
					_mmpfMutex.WaitOne();			// Attende che il mutex sia disponibile e poi lo blocca
					using(MemoryMappedViewAccessor _mmva =  _mmpf.CreateViewAccessor())
					{
						_mmva.Write<T>(0, ref val);
						ok = true;
					}
				}
				catch (Exception ex)
				{
					_sbErrMsg.AppendLine(ex.Message);
				}
				finally
				{
					_mmpfMutex.ReleaseMutex();		// Rilascia il mutex
				}
			}
			return ok;
		}

		// Vedere: https://blog.stephencleary.com/2023/09/memory-mapped-files-overlaid-structs.html

		/// <summary>
		/// Ottiene il riferimento ad un oggetto accedendovi attraverso l'handle ed un puntatore unsafe
		/// </summary>
		/// <param name="safeHandle"></param>
		/// <returns></returns>
		private static unsafe ref T Get(nint safeHandle)
		{
			return ref Unsafe.AsRef<T>(safeHandle.ToPointer());
		}

		/// <summary>
		/// Esegue l'azione del delegate sull'oggetto di tipo T attenuto dal view accessor
		/// Usare 
		/// </summary>
		/// <param name="view">MemoryMappedViewAccessor</param>
		/// <param name="action">delegate ActionRef</param>
		/// <returns></returns>
		public bool Esegui(MemoryMappedViewAccessor view, ActionRef action)
		{
			bool ok = false;
			
			if( ((_status == SharedMemoryStatus.Created)||(_status == SharedMemoryStatus.Opened)) && (_mmpfMutex != null) && (_mmpf != null))
			{
				try
				{
					_mmpfMutex.WaitOne();			// Attende che il mutex sia disponibile e poi lo blocca
					
					// Esegue l'azione del delegate sul riferimento all'oggetto ottenuto tramite handle e puntatore unsafe
					action(ref Get(view.SafeMemoryMappedViewHandle.DangerousGetHandle()));

					ok = true;
				}
				catch (Exception ex)
				{
					_sbErrMsg.AppendLine(ex.Message);
				}
				finally
				{
					view.SafeMemoryMappedViewHandle.DangerousRelease();	// Rilascia il puntatore...
					_mmpfMutex.ReleaseMutex();							// ...ed il mutex
				}
			}
			return ok;
		}
	}
}
